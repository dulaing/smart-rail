using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Operations.Incidents;
using SmartRail.Api.Tests.Access;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Operations;

public static class LinkedChangeBodies
{
    // The same request, naming the incident that explains it.
    public static object WithIncident(this object request, Guid incidentId)
    {
        var fields = JsonSerializer.SerializeToElement(request).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        fields["incidentId"] = incidentId;
        return fields;
    }
}

// A service change can name the incident that explains it. The link is checked when previewed and
// again when confirmed, a withdrawal keeps it, and a resolution cannot slip in between the check
// and the commit. PostgreSQL, real tokens, no Grilled.
public class LinkedChangeApiTests(AccessApi api) : IClassFixture<AccessApi>
{
    private sealed class FailingAudit : IAuditTrail
    {
        public void Stage(HttpContext context, Guid actorId, string action, string outcome, string targetType, Guid? targetId) =>
            throw new InvalidOperationException("audit down");

        public Task<IReadOnlyList<AuditRecord>> ReadForTargetAsync(string targetType, Guid targetId, int limit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("audit down");

        public Task<IReadOnlyList<AuditRecord>> ReadForTargetsAsync(IReadOnlyList<AuditTargets> targets, int limit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("audit down");

        public Task<AuditPage> ReadPageAsync(
            IReadOnlyList<AuditTargets> targets, DateTimeOffset? afterTime, Guid? afterId, int limit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("audit down");
    }

    private const string Incidents = "/api/operations/incidents";

    // Every test uses its own journeys, because they share one database.
    private static int journeysUsed = -1;

    private Task<(Guid JourneyId, List<StopRef> Stops)> NextJourneyAsync() =>
        api.JourneyWithStopsAsync(Interlocked.Increment(ref journeysUsed));

    private async Task<Guid> OpenStandaloneAsync(string subject) =>
        (await (await api.PostAsync(subject, Incidents, new { reasonCode = "SIGNAL_DELAY" })).JsonAsync()).GetProperty("incidentId").GetGuid();

    private async Task<Guid> OpenFromReportAsync(string controller, string driverSubject, Guid journeyId)
    {
        await api.AddDriverAsync(driverSubject, "Sunil Perera", journeyId);
        var report = await (await api.SubmitReportAsync(driverSubject, journeyId, ReportsApiExtensions.Report(Guid.NewGuid()))).JsonAsync();
        return (await (await api.PostAsync(controller, Incidents, new { sourceReportId = report.GetProperty("reportId").GetGuid() })).JsonAsync())
            .GetProperty("incidentId").GetGuid();
    }

    private async Task<JsonElement> ReadIncidentAsync(string subject, Guid incidentId) =>
        await (await api.GetAsync(subject, $"{Incidents}/{incidentId}")).JsonAsync();

    private async Task<List<Guid>> ChangeIdsOfAsync(string subject, Guid incidentId) =>
        (await ReadIncidentAsync(subject, incidentId)).GetProperty("changeIds").EnumerateArray().Select(e => e.GetGuid()).ToList();

    private async Task<int> PreviewedVersionAsync(string subject, Guid journeyId, object body) =>
        (await (await api.PreviewAsync(subject, journeyId, body)).JsonAsync()).GetProperty("previewedVersion").GetInt32();

    private async Task<JsonElement> ConfirmedAsync(string subject, Guid journeyId, object body)
    {
        var version = await PreviewedVersionAsync(subject, journeyId, body);
        var response = await api.ConfirmAsync(subject, journeyId, body.WithVersion(version));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.JsonAsync();
    }

    private async Task<ServiceChange> StoredChangeAsync(Guid changeId)
    {
        ServiceChange stored = null!;
        await api.WithDbAsync(async db => stored = await db.ServiceChanges.AsNoTracking().SingleAsync(c => c.Id == changeId));
        return stored;
    }

    [RequiresDatabaseFact]
    public async Task A_delay_names_its_incident_in_the_preview_and_after_confirming()
    {
        var controller = await api.AddAccountAsync("lnk-controller-1", "Nadeesha Fernando", roles: Roles.Controller);
        var (journeyId, _) = await NextJourneyAsync();
        var (unlinkedJourney, _) = await NextJourneyAsync();
        var incidentId = await OpenStandaloneAsync("lnk-controller-1");
        var request = ChangeBodies.Delay(20).WithIncident(incidentId);

        var preview = await (await api.PreviewAsync("lnk-controller-1", journeyId, request)).JsonAsync();
        Assert.Equal(incidentId, preview.GetProperty("incidentId").GetGuid());
        Assert.Equal((0, 0, 0), await api.CountsAsync(journeyId));
        Assert.Empty(await ChangeIdsOfAsync("lnk-controller-1", incidentId));

        var confirmed = await ConfirmedAsync("lnk-controller-1", journeyId, request);
        var changeId = confirmed.GetProperty("changeId").GetGuid();
        Assert.Equal(incidentId, confirmed.GetProperty("incidentId").GetGuid());
        Assert.Equal(20, confirmed.GetProperty("state").GetProperty("confirmedDelayMinutes").GetInt32());
        Assert.Equal(incidentId, (await StoredChangeAsync(changeId)).IncidentId);
        Assert.Equal([changeId], await ChangeIdsOfAsync("lnk-controller-1", incidentId));
        Assert.Contains(await api.AuditAsync(controller), e => e.Action == "change.publish.delay" && e.TargetId == journeyId);
        Assert.Equal((1, 1, 1), await api.CountsAsync(journeyId));

        // Staff see the link. The public sees the delay and nothing about why or who.
        var publicRead = await api.GetAsync(null, $"/api/journeys/{journeyId}/changes");
        var publicText = await publicRead.Content.ReadAsStringAsync();
        Assert.Equal(20, JsonDocument.Parse(publicText).RootElement.GetProperty("confirmedDelayMinutes").GetInt32());
        Assert.DoesNotContain(incidentId.ToString(), publicText);
        Assert.DoesNotContain("incident", publicText, StringComparison.OrdinalIgnoreCase);
        var payloads = new List<string>();
        await api.WithDbAsync(async db => payloads = await db.OutboxMessages
            .Where(m => db.ServiceChanges.Any(c => c.Id == m.ServiceChangeId && c.JourneyId == journeyId)).Select(m => m.Payload).ToListAsync());
        Assert.NotEmpty(payloads);
        Assert.All(payloads, p => Assert.DoesNotContain(incidentId.ToString(), p));

        // A change with no incident is still valid, and says so.
        var plain = await (await api.PreviewAsync("lnk-controller-1", unlinkedJourney, ChangeBodies.Delay(5))).JsonAsync();
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("incidentId").ValueKind);
        var plainConfirmed = await ConfirmedAsync("lnk-controller-1", unlinkedJourney, ChangeBodies.Delay(5));
        Assert.Equal(JsonValueKind.Null, plainConfirmed.GetProperty("incidentId").ValueKind);
        Assert.Null((await StoredChangeAsync(plainConfirmed.GetProperty("changeId").GetGuid())).IncidentId);
        Assert.Equal([changeId], await ChangeIdsOfAsync("lnk-controller-1", incidentId));
    }

    [RequiresDatabaseFact]
    public async Task One_incident_can_explain_many_changes()
    {
        await api.AddAccountAsync("lnk-controller-2", roles: Roles.Controller);
        var (reportJourney, reportStops) = await NextJourneyAsync();
        var (otherOne, _) = await NextJourneyAsync();
        var (otherTwo, _) = await NextJourneyAsync();

        // A report-backed incident explains several changes on its own journey.
        var backed = await OpenFromReportAsync("lnk-controller-2", "lnk-driver-2", reportJourney);
        var delay = (await ConfirmedAsync("lnk-controller-2", reportJourney, ChangeBodies.Delay(12).WithIncident(backed))).GetProperty("changeId").GetGuid();
        var platform = (await ConfirmedAsync("lnk-controller-2", reportJourney, ChangeBodies.Platform(reportStops[1].StopId, "6").WithIncident(backed))).GetProperty("changeId").GetGuid();
        Assert.Equal([delay, platform], await ChangeIdsOfAsync("lnk-controller-2", backed));

        // A standalone incident may explain changes on several journeys.
        var standalone = await OpenStandaloneAsync("lnk-controller-2");
        var first = (await ConfirmedAsync("lnk-controller-2", otherOne, ChangeBodies.Delay(7).WithIncident(standalone))).GetProperty("changeId").GetGuid();
        var second = (await ConfirmedAsync("lnk-controller-2", otherTwo, ChangeBodies.Cancel().WithIncident(standalone))).GetProperty("changeId").GetGuid();
        Assert.Equal([first, second], await ChangeIdsOfAsync("lnk-controller-2", standalone));

        // The list carries the same links.
        var list = await (await api.GetAsync("lnk-controller-2", $"{Incidents}?limit=100")).JsonAsync();
        var item = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("incidentId").GetGuid() == backed);
        Assert.Equal([delay, platform], item.GetProperty("changeIds").EnumerateArray().Select(e => e.GetGuid()));
    }

    [RequiresDatabaseFact]
    public async Task A_link_to_an_unknown_resolved_or_other_journey_incident_is_refused()
    {
        await api.AddAccountAsync("lnk-controller-3", roles: Roles.Controller);
        var (reportJourney, _) = await NextJourneyAsync();
        var (elsewhere, _) = await NextJourneyAsync();
        var (withHistory, _) = await NextJourneyAsync();
        var backed = await OpenFromReportAsync("lnk-controller-3", "lnk-driver-3", reportJourney);
        var resolved = await OpenStandaloneAsync("lnk-controller-3");
        await api.PostEmptyAsync("lnk-controller-3", $"{Incidents}/{resolved}/resolve");
        var open = await OpenStandaloneAsync("lnk-controller-3");
        var publisher = await api.AddAccountAsync("lnk-publisher-3", roles: Roles.Controller);
        var existing = await api.SeedChangeAsync(withHistory, 1, publisher, ChangeDetails.Delay(5));

        var cases = new (string Name, Guid Journey, Guid Incident, HttpStatusCode Expected)[]
        {
            ("unknown incident", elsewhere, Guid.NewGuid(), HttpStatusCode.NotFound),
            ("resolved incident", elsewhere, resolved, HttpStatusCode.BadRequest),
            ("a report's incident on another journey", elsewhere, backed, HttpStatusCode.BadRequest),
            ("empty incident id", elsewhere, Guid.Empty, HttpStatusCode.BadRequest),
        };
        foreach (var (name, journey, incident, expected) in cases)
        {
            var request = ChangeBodies.Delay(10).WithIncident(incident);
            Assert.True(expected == (await api.PreviewAsync("lnk-controller-3", journey, request)).StatusCode, $"preview: {name}");
            Assert.True(expected == (await api.ConfirmAsync("lnk-controller-3", journey, request.WithVersion(0))).StatusCode, $"confirm: {name}");
        }

        // A withdrawal never takes an incident, not even an open one.
        var withdrawal = ChangeBodies.Withdraw(existing).WithIncident(open);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PreviewAsync("lnk-controller-3", withHistory, withdrawal)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.ConfirmAsync("lnk-controller-3", withHistory, withdrawal.WithVersion(1))).StatusCode);

        var client = api.ClientFor(api.Token("lnk-controller-3"));
        var malformed = await client.PostAsync(
            $"/api/operations/journeys/{elsewhere}/changes/preview",
            new StringContent("""{"draft":{"kind":"CANCEL"},"incidentId":"nope"}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        // Nothing was written anywhere, and the open incident stayed unlinked.
        Assert.Equal((0, 0, 0), await api.CountsAsync(elsewhere));
        Assert.Equal((1, 1, 0), await api.CountsAsync(withHistory));
        Assert.Empty(await ChangeIdsOfAsync("lnk-controller-3", backed));
        Assert.Empty(await ChangeIdsOfAsync("lnk-controller-3", open));
        Assert.Equal(0, await api.SuccessAuditCountAsync(withHistory));
    }

    [RequiresDatabaseFact]
    public async Task Naming_an_incident_needs_the_right_to_manage_incidents()
    {
        var staff = await api.AddAccountAsync("lnk-staff-4");
        await api.AddAccountAsync("lnk-auditor-4", roles: Roles.Auditor);
        await api.AddAccountAsync("lnk-controller-4", roles: Roles.Controller);
        var (journeyId, stops) = await NextJourneyAsync();
        var (otherJourney, _) = await NextJourneyAsync();
        await api.GrantAsync(staff, Roles.StationStaff, stops[1].StationId);
        var incidentId = await OpenStandaloneAsync("lnk-controller-4");

        // Station staff may still publish their own platform change, but cannot attach an incident.
        var linked = ChangeBodies.Platform(stops[1].StopId, "4").WithIncident(incidentId);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.PreviewAsync("lnk-staff-4", journeyId, linked)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ConfirmAsync("lnk-staff-4", journeyId, linked.WithVersion(0))).StatusCode);
        Assert.Contains(await api.AuditAsync(staff), e => e is { Action: "incidents.manage", Outcome: "DENIED" });
        Assert.Equal((0, 0, 0), await api.CountsAsync(journeyId));
        Assert.Empty(await ChangeIdsOfAsync("lnk-controller-4", incidentId));

        var plain = await api.ConfirmAsync("lnk-staff-4", journeyId, ChangeBodies.Platform(stops[1].StopId, "4").WithVersion(0));
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await plain.JsonAsync()).GetProperty("incidentId").ValueKind);

        Assert.Equal(HttpStatusCode.Forbidden, (await api.PreviewAsync("lnk-auditor-4", otherJourney, ChangeBodies.Delay(5).WithIncident(incidentId))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.PreviewAsync(null, otherJourney, ChangeBodies.Delay(5).WithIncident(incidentId))).StatusCode);
        Assert.Equal((0, 0, 0), await api.CountsAsync(otherJourney));
    }

    [RequiresDatabaseFact]
    public async Task A_stale_linked_preview_is_a_409_and_attaches_nothing()
    {
        await api.AddAccountAsync("lnk-controller-5a", roles: Roles.Controller);
        await api.AddAccountAsync("lnk-controller-5b", roles: Roles.Controller);
        var (journeyId, _) = await NextJourneyAsync();
        var incidentId = await OpenStandaloneAsync("lnk-controller-5a");
        var request = ChangeBodies.Delay(30).WithIncident(incidentId);
        var version = await PreviewedVersionAsync("lnk-controller-5a", journeyId, request);

        await ConfirmedAsync("lnk-controller-5b", journeyId, ChangeBodies.Delay(3));
        var stale = await api.ConfirmAsync("lnk-controller-5a", journeyId, request.WithVersion(version));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("STALE_PREVIEW", (await stale.JsonAsync()).GetProperty("code").GetString());
        Assert.Equal((1, 1, 1), await api.CountsAsync(journeyId));
        Assert.Empty(await ChangeIdsOfAsync("lnk-controller-5a", incidentId));
    }

    [RequiresDatabaseFact]
    public async Task A_withdrawal_keeps_the_link_after_the_incident_is_resolved_and_the_change_stays_until_then()
    {
        await api.AddAccountAsync("lnk-controller-6", roles: Roles.Controller);
        var (journeyId, _) = await NextJourneyAsync();
        var incidentId = await OpenStandaloneAsync("lnk-controller-6");
        var changeId = (await ConfirmedAsync("lnk-controller-6", journeyId, ChangeBodies.Delay(25).WithIncident(incidentId))).GetProperty("changeId").GetGuid();

        // Resolving the incident does not withdraw its change.
        Assert.Equal(HttpStatusCode.OK, (await api.PostEmptyAsync("lnk-controller-6", $"{Incidents}/{incidentId}/resolve")).StatusCode);
        var stillThere = await (await api.GetAsync(null, $"/api/journeys/{journeyId}/changes")).JsonAsync();
        Assert.Equal(25, stillThere.GetProperty("confirmedDelayMinutes").GetInt32());
        Assert.Equal([changeId], await ChangeIdsOfAsync("lnk-controller-6", incidentId));
        Assert.Equal("RESOLVED", (await ReadIncidentAsync("lnk-controller-6", incidentId)).GetProperty("state").GetString());

        // Withdrawing is its own decision. The preview shows the incident it will keep.
        var withdrawal = ChangeBodies.Withdraw(changeId);
        var preview = await (await api.PreviewAsync("lnk-controller-6", journeyId, withdrawal)).JsonAsync();
        Assert.Equal(incidentId, preview.GetProperty("incidentId").GetGuid());
        var confirmed = await ConfirmedAsync("lnk-controller-6", journeyId, withdrawal);
        var withdrawalId = confirmed.GetProperty("changeId").GetGuid();

        Assert.Equal(incidentId, confirmed.GetProperty("incidentId").GetGuid());
        Assert.Equal(incidentId, (await StoredChangeAsync(withdrawalId)).IncidentId);
        Assert.Equal([changeId, withdrawalId], await ChangeIdsOfAsync("lnk-controller-6", incidentId));
        var after = await (await api.GetAsync(null, $"/api/journeys/{journeyId}/changes")).JsonAsync();
        Assert.Equal(JsonValueKind.Null, after.GetProperty("confirmedDelayMinutes").ValueKind);
        Assert.Equal("RESOLVED", (await ReadIncidentAsync("lnk-controller-6", incidentId)).GetProperty("state").GetString());
    }

    [RequiresDatabaseFact]
    public async Task Racing_a_resolution_against_a_linked_publish_leaves_one_consistent_result()
    {
        await api.AddAccountAsync("lnk-race-a", roles: Roles.Controller);
        await api.AddAccountAsync("lnk-race-b", roles: Roles.Controller);
        var won = 0;
        var lost = 0;

        for (var round = 0; round < 8; round++)
        {
            var (journeyId, _) = await NextJourneyAsync();
            var incidentId = await OpenStandaloneAsync("lnk-race-a");
            var request = ChangeBodies.Delay(15).WithIncident(incidentId);
            var version = await PreviewedVersionAsync("lnk-race-a", journeyId, request);

            var results = await Task.WhenAll(
                api.ConfirmAsync("lnk-race-a", journeyId, request.WithVersion(version)),
                api.PostEmptyAsync("lnk-race-b", $"{Incidents}/{incidentId}/resolve"));

            Assert.Equal(HttpStatusCode.OK, results[1].StatusCode);
            var incident = await ReadIncidentAsync("lnk-race-b", incidentId);
            Assert.Equal("RESOLVED", incident.GetProperty("state").GetString());
            var changeIds = incident.GetProperty("changeIds").EnumerateArray().Select(e => e.GetGuid()).ToList();

            if (results[0].StatusCode == HttpStatusCode.OK)
            {
                // The publish got in first: the change exists, is linked, and the resolution followed.
                won++;
                Assert.Equal([(await results[0].JsonAsync()).GetProperty("changeId").GetGuid()], changeIds);
                Assert.Equal((1, 1, 1), await api.CountsAsync(journeyId));
            }
            else
            {
                // The resolution won: nothing attached, and the journey is exactly as it was.
                lost++;
                Assert.Equal(HttpStatusCode.BadRequest, results[0].StatusCode);
                Assert.Empty(changeIds);
                Assert.Equal((0, 0, 0), await api.CountsAsync(journeyId));
            }
        }

        Assert.Equal(8, won + lost);
    }

    [RequiresDatabaseFact]
    public async Task A_resolution_that_lands_between_the_check_and_the_commit_wins_and_nothing_attaches()
    {
        await api.AddAccountAsync("lnk-controller-7", roles: Roles.Controller);
        var (journeyId, _) = await NextJourneyAsync();
        var incidentId = await OpenStandaloneAsync("lnk-controller-7");
        var request = ChangeBodies.Delay(40).WithIncident(incidentId);
        var version = await PreviewedVersionAsync("lnk-controller-7", journeyId, request);

        Task<HttpResponseMessage> publish = null!;
        await api.WithDbAsync(async db =>
        {
            // Someone is part-way through resolving the incident and holds its row.
            await using var resolving = await db.Database.BeginTransactionAsync();
            await db.Incidents.Where(i => i.Id == incidentId)
                .ExecuteUpdateAsync(set => set.SetProperty(i => i.Status, IncidentStates.Resolved).SetProperty(i => i.ResolvedAt, DateTimeOffset.UtcNow));

            // The publish passes its own checks, because the resolution is not committed, then has to wait.
            publish = api.ConfirmAsync("lnk-controller-7", journeyId, request.WithVersion(version));
            await Task.Delay(1000);
            Assert.False(publish.IsCompleted, "the publish should be waiting for the incident, not finished");

            await resolving.CommitAsync();
        });

        var response = await publish;
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("resolved", await response.Content.ReadAsStringAsync());
        Assert.Equal((0, 0, 0), await api.CountsAsync(journeyId));
        Assert.Empty(await ChangeIdsOfAsync("lnk-controller-7", incidentId));
    }

    [RequiresDatabaseFact]
    public async Task A_failing_audit_write_rolls_back_a_linked_publish_completely()
    {
        var actor = await api.AddAccountAsync("lnk-fail-audit", roles: Roles.Controller);
        var (journeyId, _) = await NextJourneyAsync();
        var incidentId = await OpenStandaloneAsync("lnk-fail-audit");
        var request = ChangeBodies.Delay(18).WithIncident(incidentId).WithVersion(0);
        var auditBefore = (await api.AuditAsync(actor)).Count;

        try
        {
            var response = await api.ConfirmWithAsync(
                services => services.Replace(ServiceDescriptor.Scoped<IAuditTrail, FailingAudit>()), "lnk-fail-audit", journeyId, request);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        catch (InvalidOperationException)
        {
            // The test host may surface the failure as an exception instead of a 500.
        }

        Assert.Equal((0, 0, 0), await api.CountsAsync(journeyId));
        Assert.Empty(await ChangeIdsOfAsync("lnk-fail-audit", incidentId));
        Assert.Equal(auditBefore, (await api.AuditAsync(actor)).Count);

        // The incident was not left locked or changed: the same request now succeeds.
        Assert.Equal(HttpStatusCode.OK, (await api.ConfirmAsync("lnk-fail-audit", journeyId, request)).StatusCode);
        Assert.Single(await ChangeIdsOfAsync("lnk-fail-audit", incidentId));
    }

    [RequiresDatabaseFact]
    public async Task The_database_requires_a_withdrawal_to_keep_the_incident_of_the_change_it_withdraws()
    {
        var controller = await api.AddAccountAsync("lnk-controller-9", roles: Roles.Controller);
        var (journeyId, _) = await NextJourneyAsync();
        var incidentId = await OpenStandaloneAsync("lnk-controller-9");
        var otherIncident = await OpenStandaloneAsync("lnk-controller-9");
        var publishedId = Guid.NewGuid();
        await api.WithDbAsync(async db =>
        {
            db.ServiceChanges.Add(new ServiceChange(publishedId, 1, ChangeAction.Publish, ChangeDetails.Delay(9))
            {
                JourneyId = journeyId, PublishedById = controller, PublishedAt = DateTimeOffset.UtcNow, IncidentId = incidentId,
            });
            (await db.JourneyVersions.SingleAsync(v => v.JourneyId == journeyId)).Version = 1;
            await db.SaveChangesAsync();
        });

        async Task AddWithdrawalAsync(int version, Guid? incident) => await api.WithDbAsync(async db =>
        {
            db.ServiceChanges.Add(new ServiceChange(Guid.NewGuid(), version, ChangeAction.Withdraw, new ChangeDetails(ChangeKind.Delay), publishedId)
            {
                JourneyId = journeyId, PublishedById = controller, PublishedAt = DateTimeOffset.UtcNow, IncidentId = incident,
            });
            await db.SaveChangesAsync();
        });

        foreach (var wrong in new Guid?[] { null, otherIncident })
        {
            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => AddWithdrawalAsync(2, wrong));
            Assert.Contains("keeps the incident", Assert.IsType<PostgresException>(refused.InnerException).MessageText);
        }

        await AddWithdrawalAsync(2, incidentId);
        Assert.Equal(2, (await ChangeIdsOfAsync("lnk-controller-9", incidentId)).Count);
    }
}
