using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Incidents;
using SmartRail.Api.Tests.Access;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Operations;

// Control opens an incident, from a report or on its own, and resolves it. Acknowledging a report,
// opening an incident and resolving it are three separate actions, and none changes what the
// public sees. PostgreSQL, real tokens, no Control screens.
public class IncidentApiTests(AccessApi api) : IClassFixture<AccessApi>
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

    private Task<Guid> NextJourneyAsync() => api.JourneyIdAsync(Interlocked.Increment(ref journeysUsed));

    private Task<HttpResponseMessage> OpenAsync(string? subject, object? body) => api.PostAsync(subject, Incidents, body);

    private Task<HttpResponseMessage> ResolveAsync(string? subject, Guid incidentId) =>
        api.PostEmptyAsync(subject, $"{Incidents}/{incidentId}/resolve");

    private async Task<(Guid JourneyId, Guid ReportId)> ReportedAsync(string driverSubject, string reason = "OBSTRUCTION")
    {
        var journeyId = await NextJourneyAsync();
        await api.AddDriverAsync(driverSubject, "Sunil Perera", journeyId);
        var created = await (await api.SubmitReportAsync(driverSubject, journeyId, ReportsApiExtensions.Report(Guid.NewGuid(), reason))).JsonAsync();
        return (journeyId, created.GetProperty("reportId").GetGuid());
    }

    private async Task<Incident> StoredAsync(Guid incidentId)
    {
        Incident stored = null!;
        await api.WithDbAsync(async db => stored = await db.Incidents.AsNoTracking().SingleAsync(i => i.Id == incidentId));
        return stored;
    }

    private async Task<int> IncidentAuditCountAsync(Guid incidentId, string? action = null)
    {
        var count = 0;
        await api.WithDbAsync(async db =>
            count = await db.AuditEntries.CountAsync(e =>
                e.TargetType == "INCIDENT" && e.TargetId == incidentId && e.Outcome == "SUCCEEDED" && (action == null || e.Action == action)));
        return count;
    }

    [RequiresDatabaseFact]
    public async Task An_incident_from_a_report_copies_its_reason_and_acknowledges_nothing()
    {
        var (journeyId, reportId) = await ReportedAsync("inc-driver-1", "CANNOT_CONTINUE");
        var controller = await api.AddAccountAsync("inc-controller-1", "Nadeesha Fernando", roles: Roles.Controller);
        var changesTag = (await api.GetAsync(null, $"/api/journeys/{journeyId}/changes")).Headers.ETag;
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var response = await OpenAsync("inc-controller-1", new { sourceReportId = reportId });
        var body = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var incidentId = body.GetProperty("incidentId").GetGuid();
        Assert.Equal($"{Incidents}/{incidentId}", response.Headers.Location?.OriginalString);
        Assert.Equal(reportId, body.GetProperty("sourceReportId").GetGuid());
        Assert.Equal("CANNOT_CONTINUE", body.GetProperty("reasonCode").GetString());
        Assert.Equal("OPEN", body.GetProperty("state").GetString());
        Assert.Equal(controller, body.GetProperty("openedById").GetGuid());
        Assert.Equal("Nadeesha Fernando", body.GetProperty("openedByName").GetString());
        Assert.InRange(body.GetProperty("openedAt").GetDateTimeOffset(), before, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("resolvedAt").ValueKind);

        // Opening an incident is not an acknowledgement: the driver still sees only server receipt.
        var seen = await (await api.GetAsync("inc-driver-1", $"{ReportsApiExtensions.ReportsPath(journeyId)}/{reportId}")).JsonAsync();
        Assert.Equal("SENT_AWAITING_CONTROL", seen.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("acknowledgedById").ValueKind);

        var audit = Assert.Single(await api.AuditAsync(controller));
        Assert.Equal(("incident.open", "SUCCEEDED", "INCIDENT", (Guid?)incidentId), (audit.Action, audit.Outcome, audit.TargetType, audit.TargetId));
        Assert.Equal((0, 0, 0), await api.CountsAsync(journeyId));
        Assert.Equal(changesTag, (await api.GetAsync(null, $"/api/journeys/{journeyId}/changes")).Headers.ETag);
    }

    [RequiresDatabaseFact]
    public async Task Opening_again_returns_the_existing_incident_even_after_it_is_resolved()
    {
        var (_, reportId) = await ReportedAsync("inc-driver-2");
        await api.AddAccountAsync("inc-first", "First Controller", roles: Roles.Controller);
        await api.AddAccountAsync("inc-second", "Second Controller", roles: Roles.Controller);
        var first = await (await OpenAsync("inc-first", new { sourceReportId = reportId })).JsonAsync();
        var incidentId = first.GetProperty("incidentId").GetGuid();

        var again = await OpenAsync("inc-second", new { sourceReportId = reportId });
        var resolved = await (await ResolveAsync("inc-first", incidentId)).JsonAsync();
        var afterResolved = await OpenAsync("inc-second", new { sourceReportId = reportId });

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var againBody = await again.JsonAsync();
        Assert.Equal(incidentId, againBody.GetProperty("incidentId").GetGuid());
        Assert.Equal("First Controller", againBody.GetProperty("openedByName").GetString());
        Assert.Equal("OPEN", againBody.GetProperty("state").GetString());

        Assert.Equal(HttpStatusCode.OK, afterResolved.StatusCode);
        var late = await afterResolved.JsonAsync();
        Assert.Equal(incidentId, late.GetProperty("incidentId").GetGuid());
        Assert.Equal("RESOLVED", late.GetProperty("state").GetString());
        Assert.Equal(resolved.GetProperty("resolvedAt").GetDateTimeOffset(), late.GetProperty("resolvedAt").GetDateTimeOffset());

        Assert.Equal(1, await IncidentAuditCountAsync(incidentId, "incident.open"));
    }

    [RequiresDatabaseFact]
    public async Task An_incident_can_stand_alone_with_a_valid_reason()
    {
        var controller = await api.AddAccountAsync("inc-controller-3", roles: Roles.Controller);

        var first = await OpenAsync("inc-controller-3", new { reasonCode = "SIGNAL_DELAY" });
        var second = await OpenAsync("inc-controller-3", new { reasonCode = "SIGNAL_DELAY" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var one = await first.JsonAsync();
        var two = await second.JsonAsync();
        Assert.NotEqual(one.GetProperty("incidentId").GetGuid(), two.GetProperty("incidentId").GetGuid());
        Assert.Equal(JsonValueKind.Null, one.GetProperty("sourceReportId").ValueKind);
        Assert.Equal("SIGNAL_DELAY", one.GetProperty("reasonCode").GetString());
        Assert.Equal(2, (await api.AuditAsync(controller)).Count);
    }

    [RequiresDatabaseFact]
    public async Task A_bad_incident_request_is_a_400_or_404_and_stores_nothing()
    {
        var controller = await api.AddAccountAsync("inc-controller-4", roles: Roles.Controller);
        var (_, reportId) = await ReportedAsync("inc-driver-4");
        var client = api.ClientFor(api.Token("inc-controller-4"));

        async Task<HttpStatusCode> PostRawAsync(string json) =>
            (await client.PostAsync(Incidents, new StringContent(json, System.Text.Encoding.UTF8, "application/json"))).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync("{}"));
        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync("null"));
        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync($$"""{"sourceReportId":"{{reportId}}","reasonCode":"TRAIN_FAULT"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync("""{"reasonCode":"FIRE"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync("""{"reasonCode":"train_fault"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync($$"""{"sourceReportId":"{{Guid.Empty}}"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync("""{"sourceReportId":"not-a-uuid"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync("not json"));
        Assert.Equal(HttpStatusCode.NotFound, await PostRawAsync($$"""{"sourceReportId":"{{Guid.NewGuid()}}"}"""));

        Assert.Empty(await api.AuditAsync(controller));
        await api.WithDbAsync(async db => Assert.False(await db.Incidents.AnyAsync(i => i.OpenedById == controller)));
    }

    [RequiresDatabaseFact]
    public async Task Concurrent_duplicate_creation_makes_one_incident_and_one_audit_entry()
    {
        await api.AddAccountAsync("inc-race-a", "Racer A", roles: Roles.Controller);
        await api.AddAccountAsync("inc-race-b", "Racer B", roles: Roles.Controller);

        for (var round = 0; round < 4; round++)
        {
            var (_, reportId) = await ReportedAsync($"inc-race-driver-{round}");

            var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
                OpenAsync(i % 2 == 0 ? "inc-race-a" : "inc-race-b", new { sourceReportId = reportId })));

            var statuses = responses.Select(r => r.StatusCode).ToList();
            Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.Created));
            Assert.Equal(7, statuses.Count(s => s == HttpStatusCode.OK));
            var ids = new HashSet<Guid>();
            var openers = new HashSet<Guid>();
            foreach (var response in responses)
            {
                var body = await response.JsonAsync();
                ids.Add(body.GetProperty("incidentId").GetGuid());
                openers.Add(body.GetProperty("openedById").GetGuid());
            }

            var incidentId = Assert.Single(ids);
            Assert.Single(openers);
            Assert.Equal(1, await IncidentAuditCountAsync(incidentId, "incident.open"));
            await api.WithDbAsync(async db => Assert.Equal(1, await db.Incidents.CountAsync(i => i.SourceReportId == reportId)));
        }
    }

    [RequiresDatabaseFact]
    public async Task Resolving_keeps_the_first_time_and_changes_neither_the_acknowledgement_nor_the_journey()
    {
        var (journeyId, reportId) = await ReportedAsync("inc-driver-5");
        var first = await api.AddAccountAsync("inc-resolver-1", "Resolver One", roles: Roles.Controller);
        await api.AddAccountAsync("inc-resolver-2", "Resolver Two", roles: Roles.Controller);
        var incidentId = (await (await OpenAsync("inc-resolver-1", new { sourceReportId = reportId })).JsonAsync()).GetProperty("incidentId").GetGuid();
        var changesTag = (await api.GetAsync(null, $"/api/journeys/{journeyId}/changes")).Headers.ETag;

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var response = await ResolveAsync("inc-resolver-1", incidentId);
        var body = await response.JsonAsync();
        var repeat = await (await ResolveAsync("inc-resolver-1", incidentId)).JsonAsync();
        var other = await (await ResolveAsync("inc-resolver-2", incidentId)).JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("RESOLVED", body.GetProperty("state").GetString());
        var resolvedAt = body.GetProperty("resolvedAt").GetDateTimeOffset();
        Assert.InRange(resolvedAt, before, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.True(resolvedAt >= body.GetProperty("openedAt").GetDateTimeOffset());
        Assert.Equal(first, body.GetProperty("openedById").GetGuid());
        foreach (var again in new[] { repeat, other })
        {
            Assert.Equal("RESOLVED", again.GetProperty("state").GetString());
            Assert.Equal(resolvedAt, again.GetProperty("resolvedAt").GetDateTimeOffset());
        }

        Assert.Equal(1, await IncidentAuditCountAsync(incidentId, "incident.resolve"));
        Assert.Equal(resolvedAt, (await StoredAsync(incidentId)).ResolvedAt);

        // The report was never acknowledged, so resolving the incident cannot have done it.
        var report = await (await api.GetAsync("inc-resolver-1", $"/api/operations/reports/{reportId}")).JsonAsync();
        Assert.Equal("SENT_AWAITING_CONTROL", report.GetProperty("state").GetString());
        Assert.Equal((0, 0, 0), await api.CountsAsync(journeyId));
        Assert.Equal(changesTag, (await api.GetAsync(null, $"/api/journeys/{journeyId}/changes")).Headers.ETag);
    }

    [RequiresDatabaseFact]
    public async Task An_acknowledgement_made_earlier_survives_opening_and_resolving()
    {
        var (_, reportId) = await ReportedAsync("inc-driver-6");
        await api.AddAccountAsync("inc-controller-6", "Nadeesha Fernando", roles: Roles.Controller);
        var acknowledged = await (await api.PostEmptyAsync("inc-controller-6", $"/api/operations/reports/{reportId}/acknowledge")).JsonAsync();
        var incidentId = (await (await OpenAsync("inc-controller-6", new { sourceReportId = reportId })).JsonAsync()).GetProperty("incidentId").GetGuid();

        await ResolveAsync("inc-controller-6", incidentId);

        var report = await (await api.GetAsync("inc-controller-6", $"/api/operations/reports/{reportId}")).JsonAsync();
        Assert.Equal("ACKNOWLEDGED", report.GetProperty("state").GetString());
        Assert.Equal(acknowledged.GetProperty("acknowledgedAt").GetDateTimeOffset(), report.GetProperty("acknowledgedAt").GetDateTimeOffset());
        Assert.Equal(1, await api.ReportAuditCountAsync(reportId, "report.acknowledge"));
    }

    [RequiresDatabaseFact]
    public async Task Concurrent_resolutions_share_one_time_and_one_audit_entry()
    {
        await api.AddAccountAsync("inc-res-race-a", roles: Roles.Controller);
        await api.AddAccountAsync("inc-res-race-b", roles: Roles.Controller);

        for (var round = 0; round < 4; round++)
        {
            var (_, reportId) = await ReportedAsync($"inc-res-driver-{round}");
            var incidentId = (await (await OpenAsync("inc-res-race-a", new { sourceReportId = reportId })).JsonAsync()).GetProperty("incidentId").GetGuid();

            var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
                ResolveAsync(i % 2 == 0 ? "inc-res-race-a" : "inc-res-race-b", incidentId)));

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            var times = new HashSet<DateTimeOffset>();
            foreach (var response in responses)
            {
                times.Add((await response.JsonAsync()).GetProperty("resolvedAt").GetDateTimeOffset());
            }

            Assert.Equal([(await StoredAsync(incidentId)).ResolvedAt!.Value], times);
            Assert.Equal(1, await IncidentAuditCountAsync(incidentId, "incident.resolve"));
        }
    }

    [RequiresDatabaseFact]
    public async Task An_unknown_incident_is_a_404_and_writes_no_audit_row()
    {
        var controller = await api.AddAccountAsync("inc-controller-7", roles: Roles.Controller);
        var auditor = await api.AddAccountAsync("inc-auditor-7", roles: Roles.Auditor);
        var missing = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await ResolveAsync("inc-controller-7", missing)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("inc-controller-7", $"{Incidents}/{missing}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("inc-auditor-7", $"{Incidents}/{missing}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("inc-auditor-7", $"{Incidents}/{missing}/audit")).StatusCode);

        Assert.Empty(await api.AuditAsync(controller));
        Assert.Empty(await api.AuditAsync(auditor));
    }

    [RequiresDatabaseFact]
    public async Task Incidents_list_newest_first_in_stable_pages_with_a_state_filter()
    {
        await api.AddAccountAsync("inc-controller-8", roles: Roles.Controller);
        var opener = await api.AddAccountAsync("inc-opener-8", roles: Roles.Controller);

        // Five incidents with one opening time, far enough ahead to be the newest of all.
        var now = DateTimeOffset.UtcNow;
        var tied = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero).AddDays(30);
        var tiedIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        await api.WithDbAsync(async db =>
        {
            db.Incidents.AddRange(tiedIds.Select((id, i) => new Incident
            {
                Id = id, ReasonCode = "TRAIN_FAULT", OpenedById = opener, OpenedAt = tied,
                Status = i == 0 ? IncidentStates.Resolved : IncidentStates.Open,
                ResolvedAt = i == 0 ? tied.AddMinutes(1) : null,
            }));
            await db.SaveChangesAsync();
        });
        var newest = (await (await OpenAsync("inc-controller-8", new { reasonCode = "OBSTRUCTION" })).JsonAsync()).GetProperty("incidentId").GetGuid();

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = $"{Incidents}?limit=2" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await (await api.GetAsync("inc-controller-8", url)).JsonAsync();
            Assert.True(page.GetProperty("items").GetArrayLength() <= 2);
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("incidentId").GetGuid()));
            cursor = page.GetProperty("nextCursor").GetString();
            pages++;
        }
        while (cursor is not null && pages < 50);

        int stored = 0;
        await api.WithDbAsync(async db => stored = await db.Incidents.CountAsync());
        Assert.Equal(stored, seen.Count);
        Assert.Equal(stored, seen.Distinct().Count());
        Assert.Equal(tiedIds.Order(), seen.Take(5).Order());
        Assert.Equal(newest, seen[5]);

        var open = await (await api.GetAsync("inc-controller-8", $"{Incidents}?state=OPEN&limit=100")).JsonAsync();
        var resolved = await (await api.GetAsync("inc-controller-8", $"{Incidents}?state=resolved&limit=100")).JsonAsync();
        Assert.All(open.GetProperty("items").EnumerateArray(), i => Assert.Equal("OPEN", i.GetProperty("state").GetString()));
        Assert.All(resolved.GetProperty("items").EnumerateArray(), i => Assert.Equal("RESOLVED", i.GetProperty("state").GetString()));
        Assert.Contains(tiedIds[0], resolved.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("incidentId").GetGuid()));
        Assert.DoesNotContain(tiedIds[0], open.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("incidentId").GetGuid()));

        Assert.Equal(1, (await (await api.GetAsync("inc-controller-8", $"{Incidents}?limit=0")).JsonAsync()).GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("inc-controller-8", $"{Incidents}?state=closed")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("inc-controller-8", $"{Incidents}?cursor=garbage")).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task Controllers_and_auditors_read_but_only_controllers_write_and_everyone_else_is_refused()
    {
        var (_, reportId) = await ReportedAsync("inc-driver-9");
        var controller = await api.AddAccountAsync("inc-controller-9", roles: Roles.Controller);
        await api.AddAccountAsync("inc-auditor-9", roles: Roles.Auditor);
        var incidentId = (await (await OpenAsync("inc-controller-9", new { sourceReportId = reportId })).JsonAsync()).GetProperty("incidentId").GetGuid();
        var detail = $"{Incidents}/{incidentId}";

        // Reading: both roles. The audit trail: the auditor only.
        foreach (var subject in new[] { "inc-controller-9", "inc-auditor-9" })
        {
            Assert.Equal(HttpStatusCode.OK, (await api.GetAsync(subject, Incidents)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await api.GetAsync(subject, detail)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("inc-auditor-9", $"{detail}/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("inc-controller-9", $"{detail}/audit")).StatusCode);

        // Writing: the controller only. The auditor reads the raw report inbox no more than anyone else.
        var auditorId = await AccountIdAsync("inc-auditor-9");
        var deniedBefore = (await api.AuditAsync(auditorId)).Count;
        Assert.Equal(HttpStatusCode.Forbidden, (await OpenAsync("inc-auditor-9", new { reasonCode = "TRAIN_FAULT" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ResolveAsync("inc-auditor-9", incidentId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("inc-auditor-9", "/api/operations/reports")).StatusCode);
        Assert.Equal(3, (await api.AuditAsync(auditorId)).Count - deniedBefore);

        var others = new Dictionary<string, Guid>
        {
            ["inc-driver-9"] = await AccountIdAsync("inc-driver-9"),
            ["inc-sysadmin-9"] = await api.AddAccountAsync("inc-sysadmin-9", roles: Roles.SysAdmin),
            ["inc-passenger-9"] = await api.AddAccountAsync("inc-passenger-9", roles: Roles.Passenger),
            ["inc-sim-9"] = await api.AddAccountAsync("inc-sim-9", roles: Roles.SimOperator),
            ["inc-staff-9"] = await api.AddAccountAsync("inc-staff-9"),
        };
        await api.GrantAsync(others["inc-staff-9"], Roles.StationStaff, await api.StationIdAsync("GPH"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync(null, Incidents)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await OpenAsync(null, new { reasonCode = "TRAIN_FAULT" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ResolveAsync(null, incidentId)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync(null, $"{detail}/audit")).StatusCode);

        foreach (var (subject, actor) in others)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync(subject, Incidents)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync(subject, detail)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync(subject, $"{detail}/audit")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await OpenAsync(subject, new { sourceReportId = reportId })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ResolveAsync(subject, incidentId)).StatusCode);
            Assert.Equal(5, (await api.AuditAsync(actor)).Count(e => e.Outcome == "DENIED"));
        }

        var stored = await StoredAsync(incidentId);
        Assert.Equal((IncidentStates.Open, controller), (stored.Status, stored.OpenedById));
        Assert.Equal(1, await IncidentAuditCountAsync(incidentId));
    }

    private async Task<Guid> AccountIdAsync(string subject)
    {
        var id = Guid.Empty;
        await api.WithDbAsync(async db => id = await db.Accounts.Where(a => a.Subject == subject).Select(a => a.Id).SingleAsync());
        return id;
    }

    [RequiresDatabaseFact]
    public async Task A_controller_who_loses_the_grant_is_refused_on_the_next_request()
    {
        var (_, reportId) = await ReportedAsync("inc-driver-10");
        var controller = await api.AddAccountAsync("inc-controller-10", roles: Roles.Controller);
        var incidentId = (await (await OpenAsync("inc-controller-10", new { sourceReportId = reportId })).JsonAsync()).GetProperty("incidentId").GetGuid();

        await api.WithDbAsync(db => db.RoleGrants.Where(g => g.AccountId == controller)
            .ExecuteUpdateAsync(set => set.SetProperty(g => g.ValidUntil, DateTimeOffset.UtcNow.AddMinutes(-1))));

        Assert.Equal(HttpStatusCode.Forbidden, (await ResolveAsync("inc-controller-10", incidentId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("inc-controller-10", Incidents)).StatusCode);
        Assert.Equal(IncidentStates.Open, (await StoredAsync(incidentId)).Status);
    }

    [RequiresDatabaseFact]
    public async Task The_auditor_reads_a_standalone_incidents_history_and_the_journey_trail_follows_report_backed_ones()
    {
        var (journeyId, reportId) = await ReportedAsync("inc-driver-11");
        await api.AddAccountAsync("inc-controller-11", "Nadeesha Fernando", roles: Roles.Controller);
        await api.AddAccountAsync("inc-auditor-11", roles: Roles.Auditor);
        var backed = (await (await OpenAsync("inc-controller-11", new { sourceReportId = reportId })).JsonAsync()).GetProperty("incidentId").GetGuid();
        var standalone = (await (await OpenAsync("inc-controller-11", new { reasonCode = "TRAIN_FAULT" })).JsonAsync()).GetProperty("incidentId").GetGuid();
        await api.PostEmptyAsync("inc-controller-11", $"/api/operations/reports/{reportId}/acknowledge");
        await ResolveAsync("inc-controller-11", backed);
        await ResolveAsync("inc-controller-11", standalone);
        await ResolveAsync("inc-controller-11", standalone);

        var own = await api.GetAsync("inc-auditor-11", $"{Incidents}/{standalone}/audit");
        var entries = (await own.JsonAsync()).GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(["incident.open", "incident.resolve"], entries.Select(e => e.GetProperty("action").GetString()));
        Assert.All(entries, e =>
        {
            Assert.Equal("Nadeesha Fernando", e.GetProperty("actorName").GetString());
            Assert.Equal(("SUCCEEDED", "INCIDENT", standalone), (e.GetProperty("outcome").GetString(), e.GetProperty("targetType").GetString(), e.GetProperty("targetId").GetGuid()));
            Assert.False(string.IsNullOrEmpty(e.GetProperty("correlationId").GetString()));
        });
        Assert.True(entries[0].GetProperty("occurredAt").GetDateTimeOffset() <= entries[1].GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal(1, (await (await api.GetAsync("inc-auditor-11", $"{Incidents}/{standalone}/audit?limit=1")).JsonAsync()).GetProperty("entries").GetArrayLength());

        // The journey's trail follows the report to its incident, and leaves out the standalone one.
        var journey = (await (await api.GetAsync("inc-auditor-11", $"/api/operations/journeys/{journeyId}/audit")).JsonAsync())
            .GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(
            ["report.submit", "incident.open", "report.acknowledge", "incident.resolve"],
            journey.Select(e => e.GetProperty("action").GetString()));
        Assert.DoesNotContain(journey, e => e.GetProperty("targetId").GetGuid() == standalone);
        Assert.Contains(journey, e => e.GetProperty("targetId").GetGuid() == backed);
    }

    [RequiresDatabaseFact]
    public async Task A_failing_audit_write_stores_no_incident_and_leaves_an_open_one_open()
    {
        var (_, reportId) = await ReportedAsync("inc-driver-12");
        var controller = await api.AddAccountAsync("inc-fail-audit", roles: Roles.Controller);

        async Task PostFailingAsync(string path, object body)
        {
            try
            {
                var response = await api.PostWithAsync(
                    services => services.Replace(ServiceDescriptor.Scoped<IAuditTrail, FailingAudit>()), "inc-fail-audit", path, body);
                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            }
            catch (InvalidOperationException)
            {
                // The test host may surface the failure as an exception instead of a 500.
            }
        }

        await PostFailingAsync(Incidents, new { sourceReportId = reportId });
        await api.WithDbAsync(async db => Assert.False(await db.Incidents.AnyAsync(i => i.SourceReportId == reportId)));
        Assert.Empty(await api.AuditAsync(controller));

        var opened = await OpenAsync("inc-fail-audit", new { sourceReportId = reportId });
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        var incidentId = (await opened.JsonAsync()).GetProperty("incidentId").GetGuid();

        await PostFailingAsync($"{Incidents}/{incidentId}/resolve", new { });
        var stored = await StoredAsync(incidentId);
        Assert.Equal(IncidentStates.Open, stored.Status);
        Assert.Null(stored.ResolvedAt);
        Assert.Equal(0, await IncidentAuditCountAsync(incidentId, "incident.resolve"));

        Assert.Equal(HttpStatusCode.OK, (await ResolveAsync("inc-fail-audit", incidentId)).StatusCode);
        Assert.Equal(1, await IncidentAuditCountAsync(incidentId, "incident.resolve"));
    }

    [RequiresDatabaseFact]
    public async Task The_database_refuses_a_second_incident_for_a_report_a_reopening_and_other_edits()
    {
        var (_, reportId) = await ReportedAsync("inc-driver-13");
        var opener = await api.AddAccountAsync("inc-controller-13", roles: Roles.Controller);
        var now = DateTimeOffset.UtcNow;

        Incident New(string reason = "TRAIN_FAULT", Guid? report = null, string status = IncidentStates.Open, DateTimeOffset? resolved = null) => new()
        {
            Id = Guid.NewGuid(), SourceReportId = report, ReasonCode = reason, OpenedById = opener,
            OpenedAt = now, Status = status, ResolvedAt = resolved,
        };

        async Task SaveAsync(Incident incident) => await api.WithDbAsync(async db =>
        {
            db.Incidents.Add(incident);
            await db.SaveChangesAsync();
        });

        async Task<PostgresException> RefusedAsync(Func<SmartRailDbContext, Task<int>> update)
        {
            PostgresException? refused = null;
            await api.WithDbAsync(async db =>
            {
                var exception = await Assert.ThrowsAnyAsync<Exception>(() => update(db));
                refused = exception as PostgresException ?? exception.InnerException as PostgresException;
            });
            return Assert.IsType<PostgresException>(refused);
        }

        await SaveAsync(New(report: reportId));
        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(New(report: reportId)));
        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(New(report: Guid.NewGuid())));
        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(New("FIRE")));
        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(New(status: "CLOSED")));
        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(New(status: IncidentStates.Resolved)));
        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(New(resolved: now)));
        await SaveAsync(New());
        await SaveAsync(New(status: IncidentStates.Resolved, resolved: now.AddMinutes(1)));

        var standalone = New();
        await SaveAsync(standalone);
        await api.WithDbAsync(db => db.Incidents.Where(i => i.Id == standalone.Id).ExecuteUpdateAsync(set => set
            .SetProperty(i => i.Status, IncidentStates.Resolved).SetProperty(i => i.ResolvedAt, now.AddMinutes(2))));

        Assert.Contains("final", (await RefusedAsync(db => db.Incidents.Where(i => i.Id == standalone.Id).ExecuteUpdateAsync(set => set
            .SetProperty(i => i.Status, IncidentStates.Open).SetProperty(i => i.ResolvedAt, (DateTimeOffset?)null)))).MessageText);
        Assert.Contains("final", (await RefusedAsync(db => db.Incidents.Where(i => i.Id == standalone.Id).ExecuteUpdateAsync(set => set
            .SetProperty(i => i.ResolvedAt, now.AddHours(1))))).MessageText);

        var open = New();
        await SaveAsync(open);
        Assert.Contains("only the resolution", (await RefusedAsync(db => db.Incidents.Where(i => i.Id == open.Id).ExecuteUpdateAsync(set => set
            .SetProperty(i => i.ReasonCode, "OBSTRUCTION")))).MessageText);
        Assert.Contains("only the resolution", (await RefusedAsync(db => db.Incidents.Where(i => i.Id == open.Id).ExecuteUpdateAsync(set => set
            .SetProperty(i => i.OpenedAt, now.AddHours(1))))).MessageText);
    }
}
