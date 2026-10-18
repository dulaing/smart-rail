using Microsoft.AspNetCore.Http;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Catering;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Tests.Access;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Operations;

// Confirming a change: one transaction for the version, the change, the audit row and the
// Catering message, and a 409 for anyone who confirms from an old preview. PostgreSQL, real
// tokens, no RailBites.
public class PublishApiTests(AccessApi api) : IClassFixture<AccessApi>
{
    private sealed class FailingOutbox : ICateringOutbox
    {
        public void Enqueue(TrainChangeMessage message) => throw new InvalidOperationException("outbox down");
    }

    private sealed class FailingAudit : IAuditTrail
    {
        public void Stage(HttpContext context, Guid actorId, string action, string outcome, string targetType, Guid? targetId) =>
            throw new InvalidOperationException("audit down");

        public Task<IReadOnlyList<AuditRecord>> ReadForTargetAsync(string targetType, Guid targetId, int limit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("audit down");

        public Task<IReadOnlyList<AuditRecord>> ReadForTargetsAsync(IReadOnlyList<AuditTargets> targets, int limit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("audit down");
    }

    // Every test uses its own journey, because they share one database.
    private static int journeysUsed = -1;

    private Task<(Guid JourneyId, List<StopRef> Stops)> NextJourneyAsync() =>
        api.JourneyWithStopsAsync(Interlocked.Increment(ref journeysUsed));

    private async Task<int> PreviewedVersionAsync(string subject, Guid journeyId, object body) =>
        (await (await api.PreviewAsync(subject, journeyId, body)).JsonAsync()).GetProperty("previewedVersion").GetInt32();

    private async Task<List<JsonDocument>> OutboxPayloadsAsync(Guid journeyId)
    {
        var payloads = new List<string>();
        await api.WithDbAsync(async db =>
            payloads = await db.OutboxMessages.Where(m => db.ServiceChanges.Any(c => c.Id == m.ServiceChangeId && c.JourneyId == journeyId))
                .OrderBy(m => m.CreatedAt).Select(m => m.Payload).ToListAsync());
        return payloads.Select(p => JsonDocument.Parse(p)).ToList();
    }

    [RequiresDatabaseFact]
    public async Task A_delay_is_confirmed_with_its_audit_row_and_catering_message()
    {
        var actor = await api.AddAccountAsync("pub-controller-1", roles: Roles.Controller);
        var (journeyId, stops) = await NextJourneyAsync();

        var version = await PreviewedVersionAsync("pub-controller-1", journeyId, ChangeBodies.Delay(25));
        var response = await api.ConfirmAsync("pub-controller-1", journeyId, ChangeBodies.Delay(25).WithVersion(version));
        var body = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, body.GetProperty("journeyVersion").GetInt32());
        Assert.Equal(25, body.GetProperty("state").GetProperty("confirmedDelayMinutes").GetInt32());
        var changeId = body.GetProperty("changeId").GetGuid();

        Assert.Equal((1, 1, 1), await api.CountsAsync(journeyId));
        ServiceChange stored = null!;
        await api.WithDbAsync(async db => stored = await db.ServiceChanges.AsNoTracking().SingleAsync(c => c.Id == changeId));
        Assert.Equal((actor, ChangeAction.Publish, 25, 1), (stored.PublishedById, stored.Action, stored.DelayMinutes, stored.JourneyVersion));
        Assert.True(stored.PublishedAt > DateTimeOffset.UtcNow.AddMinutes(-1));

        var audit = Assert.Single(await api.AuditAsync(actor));
        Assert.Equal(("change.publish.delay", "SUCCEEDED", "JOURNEY", (Guid?)journeyId), (audit.Action, audit.Outcome, audit.TargetType, audit.TargetId));

        var message = Assert.Single(await OutboxPayloadsAsync(journeyId)).RootElement;
        var journey = message.GetProperty("journey");
        Assert.Equal(1, journey.GetProperty("version").GetInt32());
        Assert.Equal("DELAY", message.GetProperty("eventType").GetString());
        Assert.Equal($"train-change:{changeId}", message.GetProperty("eventId").GetString());
        Assert.False(journey.GetProperty("orderClosed").GetBoolean());
        Assert.Equal(0, journey.GetProperty("blockedPickupStopIds").GetArrayLength());

        // Every stop with a scheduled arrival carries its time. This seeded journey has no sighting,
        // so the estimates are unknown (null), never filled in from the schedule.
        var pickups = message.GetProperty("stops").EnumerateArray().ToList();
        Assert.Equal(stops.Count - 1, pickups.Count);
        Assert.All(pickups, stop =>
        {
            Assert.NotEqual(JsonValueKind.Null, stop.GetProperty("scheduledArrival").ValueKind);
            Assert.Equal(JsonValueKind.Null, stop.GetProperty("estimatedArrival").ValueKind);
            Assert.Equal(JsonValueKind.Null, stop.GetProperty("etaEvidenceAt").ValueKind);
        });
        Assert.NotEqual(JsonValueKind.Null, message.GetProperty("calculatedAt").ValueKind);

        // The public read now shows it, with no staff identity.
        var state = await (await api.Client.GetAsync($"/api/journeys/{journeyId}/changes")).JsonAsync();
        Assert.Equal(25, state.GetProperty("confirmedDelayMinutes").GetInt32());
        Assert.DoesNotContain(actor.ToString(), state.GetRawText());
    }

    [RequiresDatabaseFact]
    public async Task Cancel_and_early_end_messages_carry_the_full_eligibility_snapshot()
    {
        await api.AddAccountAsync("pub-controller-2", roles: Roles.Controller);
        var (cancelJourney, _) = await NextJourneyAsync();
        var (earlyJourney, stops) = await NextJourneyAsync();

        await api.ConfirmAsync("pub-controller-2", cancelJourney, ChangeBodies.Cancel().WithVersion(0));
        await api.ConfirmAsync("pub-controller-2", earlyJourney, ChangeBodies.EarlyEnd(stops[1].StopId).WithVersion(0));

        var cancel = Assert.Single(await OutboxPayloadsAsync(cancelJourney)).RootElement;
        Assert.True(cancel.GetProperty("journey").GetProperty("orderClosed").GetBoolean());
        Assert.Equal("CANCEL", cancel.GetProperty("eventType").GetString());
        Assert.Equal(0, cancel.GetProperty("journey").GetProperty("blockedPickupStopIds").GetArrayLength());

        var early = Assert.Single(await OutboxPayloadsAsync(earlyJourney)).RootElement;
        Assert.False(early.GetProperty("journey").GetProperty("orderClosed").GetBoolean());
        Assert.Equal("EARLY_END", early.GetProperty("eventType").GetString());
        Assert.Equal(
            stops.Skip(2).Select(s => s.StopId),
            early.GetProperty("journey").GetProperty("blockedPickupStopIds").EnumerateArray().Select(e => e.GetGuid()));
    }

    [RequiresDatabaseFact]
    public async Task A_platform_change_by_station_staff_needs_no_catering_message()
    {
        var staff = await api.AddAccountAsync("pub-staff-1");
        var (journeyId, stops) = await NextJourneyAsync();
        await api.GrantAsync(staff, Roles.StationStaff, stops[1].StationId);

        var response = await api.ConfirmAsync("pub-staff-1", journeyId, ChangeBodies.Platform(stops[1].StopId, "5").WithVersion(0));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((1, 1, 0), await api.CountsAsync(journeyId));
        Assert.Equal("5", (await response.JsonAsync()).GetProperty("state").GetProperty("stops")[1].GetProperty("platform").GetString());
    }

    [RequiresDatabaseFact]
    public async Task Confirming_without_permission_writes_nothing()
    {
        await api.AddAccountAsync("pub-auditor", roles: Roles.Auditor);
        var (journeyId, _) = await NextJourneyAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.ConfirmAsync(null, journeyId, ChangeBodies.Delay(5).WithVersion(0))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ConfirmAsync("pub-auditor", journeyId, ChangeBodies.Delay(5).WithVersion(0))).StatusCode);
        Assert.Equal((0, 0, 0), await api.CountsAsync(journeyId));
        Assert.Equal(0, await api.SuccessAuditCountAsync(journeyId));
    }

    [RequiresDatabaseFact]
    public async Task A_confirmation_needs_a_previewed_version_and_a_valid_draft()
    {
        await api.AddAccountAsync("pub-controller-3", roles: Roles.Controller);
        var (journeyId, _) = await NextJourneyAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await api.ConfirmAsync("pub-controller-3", journeyId, ChangeBodies.Delay(5))).StatusCode);
        // An invalid draft is a 400 whatever version it carries, not a 409.
        Assert.Equal(HttpStatusCode.BadRequest, (await api.ConfirmAsync("pub-controller-3", journeyId, ChangeBodies.Delay(-5).WithVersion(99))).StatusCode);
        Assert.Equal((0, 0, 0), await api.CountsAsync(journeyId));
    }

    [RequiresDatabaseFact]
    public async Task An_old_preview_gets_409_with_the_latest_version_and_a_new_preview_succeeds()
    {
        await api.AddAccountAsync("pub-a", roles: Roles.Controller);
        await api.AddAccountAsync("pub-b", roles: Roles.Controller);
        var (journeyId, stops) = await NextJourneyAsync();

        var seenByA = await PreviewedVersionAsync("pub-a", journeyId, ChangeBodies.Delay(10));
        var seenByB = await PreviewedVersionAsync("pub-b", journeyId, ChangeBodies.EarlyEnd(stops[1].StopId));
        Assert.Equal(seenByA, seenByB);

        Assert.Equal(HttpStatusCode.OK, (await api.ConfirmAsync("pub-a", journeyId, ChangeBodies.Delay(10).WithVersion(seenByA))).StatusCode);

        var stale = await api.ConfirmAsync("pub-b", journeyId, ChangeBodies.EarlyEnd(stops[1].StopId).WithVersion(seenByB));
        var problem = await stale.JsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("STALE_PREVIEW", problem.GetProperty("code").GetString());
        Assert.Equal(0, problem.GetProperty("previewedVersion").GetInt32());
        Assert.Equal(1, problem.GetProperty("latestVersion").GetInt32());
        Assert.Equal((1, 1, 1), await api.CountsAsync(journeyId));
        Assert.Equal(1, await api.SuccessAuditCountAsync(journeyId));

        // The loser previews again, sees the delay already in force, and may then confirm.
        var fresh = await PreviewedVersionAsync("pub-b", journeyId, ChangeBodies.EarlyEnd(stops[1].StopId));
        Assert.Equal(1, fresh);
        var again = await api.ConfirmAsync("pub-b", journeyId, ChangeBodies.EarlyEnd(stops[1].StopId).WithVersion(fresh));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(2, (await again.JsonAsync()).GetProperty("journeyVersion").GetInt32());
        Assert.Equal((2, 2, 2), await api.CountsAsync(journeyId));
    }

    public static TheoryData<string> Pairs => new() { "same kind", "unrelated kinds", "cancel and early end" };

    [RequiresDatabaseTheory]
    [MemberData(nameof(Pairs))]
    public async Task Two_controllers_confirming_at_once_give_exactly_one_success_and_one_409(string pair)
    {
        var raceA = $"race-a-{pair}";
        var raceB = $"race-b-{pair}";
        await api.AddAccountAsync(raceA, roles: Roles.Controller);
        await api.AddAccountAsync(raceB, roles: Roles.Controller);

        // Several rounds, each on its own journey, so the race is tried more than once.
        for (var round = 0; round < 5; round++)
        {
            var (journeyId, stops) = await NextJourneyAsync();
            var (first, second) = pair switch
            {
                "same kind" => (ChangeBodies.Delay(10), ChangeBodies.Delay(30)),
                "unrelated kinds" => (ChangeBodies.Delay(10), ChangeBodies.Platform(stops[1].StopId, "7")),
                _ => (ChangeBodies.Cancel(), ChangeBodies.EarlyEnd(stops[1].StopId)),
            };
            var versionA = await PreviewedVersionAsync(raceA, journeyId, first);
            var versionB = await PreviewedVersionAsync(raceB, journeyId, second);

            var results = await Task.WhenAll(
                api.ConfirmAsync(raceA, journeyId, first.WithVersion(versionA)),
                api.ConfirmAsync(raceB, journeyId, second.WithVersion(versionB)));

            var statuses = results.Select(r => r.StatusCode).Order().ToList();
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses);

            var loser = results.Single(r => r.StatusCode == HttpStatusCode.Conflict);
            var problem = await loser.JsonAsync();
            Assert.Equal("STALE_PREVIEW", problem.GetProperty("code").GetString());
            Assert.Equal(1, problem.GetProperty("latestVersion").GetInt32());

            // One version step, one history row, one success audit, and a message only if the
            // winning change needs one (a platform change does not).
            var winnerIsPlatform = pair == "unrelated kinds" && results[1].StatusCode == HttpStatusCode.OK;
            Assert.Equal((1, 1, winnerIsPlatform ? 0 : 1), await api.CountsAsync(journeyId));
            Assert.Equal(1, await api.SuccessAuditCountAsync(journeyId));
        }
    }

    [RequiresDatabaseFact]
    public async Task A_failing_outbox_rolls_back_the_whole_confirmation()
    {
        var actor = await api.AddAccountAsync("fail-outbox", roles: Roles.Controller);
        var (journeyId, _) = await NextJourneyAsync();

        await AssertRolledBackAsync(
            services => services.Replace(ServiceDescriptor.Scoped<ICateringOutbox, FailingOutbox>()), "fail-outbox", journeyId);

        Assert.Empty(await api.AuditAsync(actor));
        await AssertThenConfirmsNormallyAsync("fail-outbox", journeyId);
    }

    [RequiresDatabaseFact]
    public async Task A_failing_audit_write_rolls_back_the_whole_confirmation()
    {
        var actor = await api.AddAccountAsync("fail-audit", roles: Roles.Controller);
        var (journeyId, _) = await NextJourneyAsync();

        await AssertRolledBackAsync(
            services => services.Replace(ServiceDescriptor.Scoped<IAuditTrail, FailingAudit>()), "fail-audit", journeyId);

        Assert.Empty(await api.AuditAsync(actor));
        await AssertThenConfirmsNormallyAsync("fail-audit", journeyId);
    }

    private async Task AssertRolledBackAsync(Action<IServiceCollection> replace, string subject, Guid journeyId)
    {
        try
        {
            var response = await api.ConfirmWithAsync(replace, subject, journeyId, ChangeBodies.Cancel().WithVersion(0));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        catch (InvalidOperationException)
        {
            // The test host may surface the failure as an exception instead of a 500.
        }

        Assert.Equal((0, 0, 0), await api.CountsAsync(journeyId));
        Assert.Equal(0, await api.SuccessAuditCountAsync(journeyId));
    }

    // The failed attempt left the version alone, so the same preview is still good.
    private async Task AssertThenConfirmsNormallyAsync(string subject, Guid journeyId)
    {
        var response = await api.ConfirmAsync(subject, journeyId, ChangeBodies.Cancel().WithVersion(0));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((1, 1, 1), await api.CountsAsync(journeyId));
    }
}
