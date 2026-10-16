using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Tests.Access;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Operations;

// Withdrawing a change through the same preview and confirm path, and the protected audit read.
public class WithdrawAndAuditApiTests(AccessApi api) : IClassFixture<AccessApi>
{
    // Every test uses its own journey, because they share one database.
    private static int journeysUsed = -1;

    private Task<(Guid JourneyId, List<StopRef> Stops)> NextJourneyAsync() =>
        api.JourneyWithStopsAsync(Interlocked.Increment(ref journeysUsed));

    // Previews, then confirms at the version the preview showed. Returns the confirmed body.
    private async Task<JsonElement> PublishAsync(string subject, Guid journeyId, object body)
    {
        var preview = await api.PreviewAsync(subject, journeyId, body);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var version = (await preview.JsonAsync()).GetProperty("previewedVersion").GetInt32();

        var response = await api.ConfirmAsync(subject, journeyId, body.WithVersion(version));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.JsonAsync();
    }

    private async Task<List<JsonElement>> OutboxAsync(Guid journeyId)
    {
        var payloads = new List<string>();
        await api.WithDbAsync(async db =>
            payloads = await db.OutboxMessages.Where(m => db.ServiceChanges.Any(c => c.Id == m.ServiceChangeId && c.JourneyId == journeyId))
                .OrderBy(m => m.CreatedAt).Select(m => m.Payload).ToListAsync());
        return payloads.Select(p => JsonDocument.Parse(p).RootElement).ToList();
    }

    [RequiresDatabaseFact]
    public async Task Withdrawing_an_early_end_restores_only_that_override_and_keeps_history()
    {
        await api.AddAccountAsync("wd-controller-1", roles: Roles.Controller);
        var (journeyId, stops) = await NextJourneyAsync();
        await PublishAsync("wd-controller-1", journeyId, ChangeBodies.Platform(stops[1].StopId, "5"));
        await PublishAsync("wd-controller-1", journeyId, ChangeBodies.Delay(15));
        var early = await PublishAsync("wd-controller-1", journeyId, ChangeBodies.EarlyEnd(stops[1].StopId));
        var earlyId = early.GetProperty("changeId").GetGuid();
        Assert.Equal("ENDS_EARLY", early.GetProperty("state").GetProperty("availability").GetString());

        // The preview shows the stops that would come back and what stays.
        var preview = await (await api.PreviewAsync("wd-controller-1", journeyId, ChangeBodies.Withdraw(earlyId))).JsonAsync();
        Assert.Equal("WITHDRAW", preview.GetProperty("action").GetString());
        Assert.Equal(3, preview.GetProperty("previewedVersion").GetInt32());
        Assert.Equal(
            stops.Skip(2).Select(s => s.StopId),
            preview.GetProperty("affectedStops").EnumerateArray().Select(s => s.GetProperty("stopId").GetGuid()));

        var done = await PublishAsync("wd-controller-1", journeyId, ChangeBodies.Withdraw(earlyId));

        Assert.Equal(4, done.GetProperty("journeyVersion").GetInt32());
        var state = done.GetProperty("state");
        Assert.Equal("AS_SCHEDULED", state.GetProperty("availability").GetString());
        Assert.Equal(15, state.GetProperty("confirmedDelayMinutes").GetInt32());
        Assert.Equal("5", state.GetProperty("stops")[1].GetProperty("platform").GetString());
        Assert.All(state.GetProperty("stops").EnumerateArray(), s => Assert.True(s.GetProperty("served").GetBoolean()));
        Assert.Equal(2, state.GetProperty("activeChanges").GetArrayLength());

        // History keeps every row, in order; the withdrawn change is still there, unedited.
        var history = new List<(int Version, ChangeAction Action, ChangeKind Kind, Guid? Supersedes)>();
        await api.WithDbAsync(async db =>
            history = (await db.ServiceChanges.AsNoTracking().Where(c => c.JourneyId == journeyId).OrderBy(c => c.JourneyVersion).ToListAsync())
                .Select(c => (c.JourneyVersion, c.Action, c.Kind, c.SupersedesChangeId)).ToList());
        Assert.Equal(
            [
                (1, ChangeAction.Publish, ChangeKind.Platform, (Guid?)null),
                (2, ChangeAction.Publish, ChangeKind.Delay, null),
                (3, ChangeAction.Publish, ChangeKind.EarlyEnd, null),
                (4, ChangeAction.Withdraw, ChangeKind.EarlyEnd, earlyId),
            ],
            history);

        // The withdrawal's message is newer and reopens what the early ending had blocked.
        var messages = await OutboxAsync(journeyId);
        Assert.Equal(3, messages.Count);
        var ending = messages[1];
        var withdrawal = messages[2];
        Assert.Equal(3, ending.GetProperty("journey_version").GetInt32());
        Assert.Equal(stops.Count - 2, ending.GetProperty("blocked_pickup_stop_ids").GetArrayLength());
        Assert.Equal(4, withdrawal.GetProperty("journey_version").GetInt32());
        Assert.Equal("WITHDRAW", withdrawal.GetProperty("action").GetString());
        Assert.Equal(0, withdrawal.GetProperty("blocked_pickup_stop_ids").GetArrayLength());
        Assert.False(withdrawal.GetProperty("order_closed").GetBoolean());
    }

    [RequiresDatabaseFact]
    public async Task A_stale_or_repeated_withdrawal_is_refused_and_writes_nothing()
    {
        await api.AddAccountAsync("wd-a", roles: Roles.Controller);
        await api.AddAccountAsync("wd-b", roles: Roles.Controller);
        var (journeyId, _) = await NextJourneyAsync();
        var first = (await PublishAsync("wd-a", journeyId, ChangeBodies.Delay(10))).GetProperty("changeId").GetGuid();
        var second = (await PublishAsync("wd-a", journeyId, ChangeBodies.Delay(25))).GetProperty("changeId").GetGuid();

        // B previews withdrawing the 25-minute delay at version 2; A then changes something else.
        var seenByB = (await (await api.PreviewAsync("wd-b", journeyId, ChangeBodies.Withdraw(second))).JsonAsync())
            .GetProperty("previewedVersion").GetInt32();
        await PublishAsync("wd-a", journeyId, ChangeBodies.Cancel());

        var stale = await api.ConfirmAsync("wd-b", journeyId, ChangeBodies.Withdraw(second).WithVersion(seenByB));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("STALE_PREVIEW", (await stale.JsonAsync()).GetProperty("code").GetString());

        // The replaced 10-minute delay cannot be withdrawn, and neither can one already withdrawn.
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PreviewAsync("wd-b", journeyId, ChangeBodies.Withdraw(first))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.ConfirmAsync("wd-b", journeyId, ChangeBodies.Withdraw(second).WithVersion(3))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PreviewAsync("wd-b", journeyId, ChangeBodies.Withdraw(second))).StatusCode);
        // Version first: from an old version it is a 409, from the current one a 400.
        Assert.Equal(HttpStatusCode.Conflict, (await api.ConfirmAsync("wd-b", journeyId, ChangeBodies.Withdraw(second).WithVersion(3))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.ConfirmAsync("wd-b", journeyId, ChangeBodies.Withdraw(second).WithVersion(4))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.ConfirmAsync("wd-b", journeyId, ChangeBodies.Withdraw(first).WithVersion(4))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.ConfirmAsync("wd-b", journeyId, ChangeBodies.Withdraw(Guid.NewGuid()).WithVersion(4))).StatusCode);

        // Delay 10, delay 25, cancel, one withdrawal: four steps, no delay left, still cancelled.
        Assert.Equal((4, 4, 4), await api.CountsAsync(journeyId));
        var state = await (await api.Client.GetAsync($"/api/journeys/{journeyId}/changes")).JsonAsync();
        Assert.Equal(JsonValueKind.Null, state.GetProperty("confirmedDelayMinutes").ValueKind);
        Assert.Equal("CANCELLED", state.GetProperty("availability").GetString());
    }

    [RequiresDatabaseFact]
    public async Task Platform_staff_may_withdraw_a_platform_change_at_their_station_only()
    {
        await api.AddAccountAsync("wd-controller-2", roles: Roles.Controller);
        var staff = await api.AddAccountAsync("wd-staff");
        var (journeyId, stops) = await NextJourneyAsync();
        await api.GrantAsync(staff, Roles.StationStaff, stops[1].StationId);
        var own = (await PublishAsync("wd-staff", journeyId, ChangeBodies.Platform(stops[1].StopId, "5"))).GetProperty("changeId").GetGuid();
        var other = (await PublishAsync("wd-controller-2", journeyId, ChangeBodies.Platform(stops[2].StopId, "6"))).GetProperty("changeId").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await api.ConfirmAsync("wd-staff", journeyId, ChangeBodies.Withdraw(other).WithVersion(2))).StatusCode);
        var done = await api.ConfirmAsync("wd-staff", journeyId, ChangeBodies.Withdraw(own).WithVersion(2));

        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        var state = (await done.JsonAsync()).GetProperty("state");
        Assert.Equal(JsonValueKind.Null, state.GetProperty("stops")[1].GetProperty("confirmedPlatform").ValueKind);
        Assert.Equal("6", state.GetProperty("stops")[2].GetProperty("platform").GetString());
    }

    [RequiresDatabaseFact]
    public async Task An_auditor_sees_who_acted_including_a_denied_write_that_changed_nothing()
    {
        var controller = await api.AddAccountAsync("au-controller", "Dilani Controller", roles: Roles.Controller);
        var admin = await api.AddAccountAsync("au-admin", "Sam Admin", roles: Roles.SysAdmin);
        await api.AddAccountAsync("au-auditor", "Ada Auditor", roles: Roles.Auditor);
        var (journeyId, _) = await NextJourneyAsync();
        var delay = (await PublishAsync("au-controller", journeyId, ChangeBodies.Delay(20))).GetProperty("changeId").GetGuid();

        var denied = await api.ConfirmAsync("au-admin", journeyId, ChangeBodies.Cancel().WithVersion(1));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await PublishAsync("au-controller", journeyId, ChangeBodies.Withdraw(delay));
        var versionBefore = await api.CountsAsync(journeyId);

        using var client = api.ClientFor(api.Token("au-auditor"));
        var response = await client.GetAsync($"/api/operations/journeys/{journeyId}/audit");
        var entries = (await response.JsonAsync()).GetProperty("entries").EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [("change.publish.delay", "SUCCEEDED", "Dilani Controller"),
             ("changes.publish", "DENIED", "Sam Admin"),
             ("change.withdraw.delay", "SUCCEEDED", "Dilani Controller")],
            entries.Select(e => (e.GetProperty("action").GetString()!, e.GetProperty("outcome").GetString()!, e.GetProperty("actorName").GetString()!)));
        Assert.Equal(
            [controller, admin, controller],
            entries.Select(e => e.GetProperty("actorId").GetGuid()));
        Assert.All(entries, e =>
        {
            Assert.Equal("JOURNEY", e.GetProperty("targetType").GetString());
            Assert.Equal(journeyId, e.GetProperty("targetId").GetGuid());
            Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("correlationId").GetString()));
            Assert.NotEqual(default, e.GetProperty("occurredAt").GetDateTimeOffset());
        });
        var times = entries.Select(e => e.GetProperty("occurredAt").GetDateTimeOffset()).ToList();
        Assert.Equal(times.Order(), times);

        // The denied write left the journey as it was, and reading the trail changed nothing.
        Assert.Equal(versionBefore, await api.CountsAsync(journeyId));
        Assert.Equal(2, versionBefore.Version);
    }

    [RequiresDatabaseFact]
    public async Task The_audit_read_is_for_auditors_only_and_is_bounded()
    {
        await api.AddAccountAsync("au2-controller", roles: Roles.Controller);
        await api.AddAccountAsync("au2-auditor", roles: Roles.Auditor);
        await api.AddAccountAsync("au2-passenger", roles: Roles.Passenger);
        var (journeyId, _) = await NextJourneyAsync();
        await PublishAsync("au2-controller", journeyId, ChangeBodies.Delay(5));
        await PublishAsync("au2-controller", journeyId, ChangeBodies.Delay(6));
        await PublishAsync("au2-controller", journeyId, ChangeBodies.Delay(7));
        var path = $"/api/operations/journeys/{journeyId}/audit";

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.ClientFor(null).GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor(api.Token("au2-controller")).GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor(api.Token("au2-passenger")).GetAsync(path)).StatusCode);

        var auditor = api.ClientFor(api.Token("au2-auditor"));
        Assert.Equal(3, (await (await auditor.GetAsync(path)).JsonAsync()).GetProperty("entries").GetArrayLength());
        Assert.Equal(2, (await (await auditor.GetAsync($"{path}?limit=2")).JsonAsync()).GetProperty("entries").GetArrayLength());
        Assert.Equal(1, (await (await auditor.GetAsync($"{path}?limit=0")).JsonAsync()).GetProperty("entries").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await auditor.GetAsync($"/api/operations/journeys/{Guid.NewGuid()}/audit")).StatusCode);
    }
}
