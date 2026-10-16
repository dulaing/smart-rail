using System.Net;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Tests.Access;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Operations;

// Previewing a change with real tokens, accounts and grants on a disposable PostgreSQL. A
// preview never changes the journey.
public class PreviewApiTests(AccessApi api) : IClassFixture<AccessApi>
{
    [RequiresDatabaseFact]
    public async Task A_controller_gets_before_and_after_and_nothing_is_changed()
    {
        var controller = await api.AddAccountAsync("pv-controller-1", roles: Roles.Controller);
        var (journeyId, stops) = await api.JourneyWithStopsAsync(0);

        var response = await api.PreviewAsync("pv-controller-1", journeyId, ChangeBodies.Delay(25));
        var body = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("PUBLISH", body.GetProperty("action").GetString());
        Assert.Equal("DELAY", body.GetProperty("change").GetProperty("kind").GetString());
        Assert.Equal(0, body.GetProperty("previewedVersion").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("before").GetProperty("confirmedDelayMinutes").ValueKind);
        Assert.Equal(25, body.GetProperty("after").GetProperty("confirmedDelayMinutes").GetInt32());
        Assert.Equal(stops.Count, body.GetProperty("affectedStops").GetArrayLength());
        Assert.Equal(3, body.GetProperty("screens").GetArrayLength());
        Assert.True(body.GetProperty("food").GetProperty("etaChanges").GetBoolean());

        // Nothing stored, no version moved, nothing audited for a successful preview.
        Assert.Equal((0, 0), await api.CountsAsync(journeyId));
        Assert.Empty(await api.AuditAsync(controller));
        var state = await api.Client.GetAsync($"/api/journeys/{journeyId}/changes");
        Assert.Equal(0, (await state.JsonAsync()).GetProperty("activeChanges").GetArrayLength());
    }

    [RequiresDatabaseFact]
    public async Task An_early_end_preview_names_the_final_station_and_the_unserved_stops()
    {
        await api.AddAccountAsync("pv-controller-2", roles: Roles.Controller);
        var (journeyId, stops) = await api.JourneyWithStopsAsync(0);

        var response = await api.PreviewAsync("pv-controller-2", journeyId, ChangeBodies.EarlyEnd(stops[1].StopId));
        var early = (await response.JsonAsync()).GetProperty("earlyEnd");

        Assert.Equal(stops[1].StationName, early.GetProperty("finalStationName").GetString());
        Assert.Equal(
            stops.Skip(2).Select(s => s.StopId),
            early.GetProperty("unservedStops").EnumerateArray().Select(s => s.GetProperty("stopId").GetGuid()));
    }

    [RequiresDatabaseFact]
    public async Task A_withdrawal_preview_describes_the_change_it_would_withdraw()
    {
        var controller = await api.AddAccountAsync("pv-controller-3", roles: Roles.Controller);
        var (journeyId, stops) = await api.JourneyWithStopsAsync(1);
        var changeId = await api.SeedChangeAsync(journeyId, 1, controller, ChangeDetails.EarlyEnd(stops[1].StopId));

        var response = await api.PreviewAsync("pv-controller-3", journeyId, ChangeBodies.Withdraw(changeId));
        var body = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("WITHDRAW", body.GetProperty("action").GetString());
        Assert.Equal(changeId, body.GetProperty("withdrawsChangeId").GetGuid());
        Assert.Equal(1, body.GetProperty("previewedVersion").GetInt32());
        Assert.Equal("ENDS_EARLY", body.GetProperty("before").GetProperty("availability").GetString());
        Assert.Equal("AS_SCHEDULED", body.GetProperty("after").GetProperty("availability").GetString());
        Assert.Equal((1, 1), await api.CountsAsync(journeyId));
    }

    [RequiresDatabaseFact]
    public async Task Platform_staff_may_preview_a_platform_change_only_at_their_station()
    {
        var staff = await api.AddAccountAsync("pv-staff-1");
        var (journeyId, stops) = await api.JourneyWithStopsAsync(2);
        await api.GrantAsync(staff, Roles.StationStaff, stops[1].StationId);

        var own = await api.PreviewAsync("pv-staff-1", journeyId, ChangeBodies.Platform(stops[1].StopId, "5"));
        var other = await api.PreviewAsync("pv-staff-1", journeyId, ChangeBodies.Platform(stops[2].StopId, "5"));
        var delay = await api.PreviewAsync("pv-staff-1", journeyId, ChangeBodies.Delay(10));
        var cancel = await api.PreviewAsync("pv-staff-1", journeyId, ChangeBodies.Cancel());
        var earlyEnd = await api.PreviewAsync("pv-staff-1", journeyId, ChangeBodies.EarlyEnd(stops[1].StopId));

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal("5", (await own.JsonAsync()).GetProperty("after").GetProperty("stops")[1].GetProperty("platform").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delay.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, earlyEnd.StatusCode);

        // Each refusal is audited once against the journey, with who and what but no body.
        var denied = (await api.AuditAsync(staff)).Where(e => e.Outcome == "DENIED").ToList();
        Assert.Equal(4, denied.Count);
        Assert.All(denied, e =>
        {
            Assert.Equal(("changes.publish", "JOURNEY", (Guid?)journeyId), (e.Action, e.TargetType, e.TargetId));
            Assert.False(string.IsNullOrWhiteSpace(e.CorrelationId));
        });
    }

    [RequiresDatabaseFact]
    public async Task Platform_staff_may_preview_withdrawing_a_platform_change_at_their_station_only()
    {
        var controller = await api.AddAccountAsync("pv-controller-4", roles: Roles.Controller);
        var staff = await api.AddAccountAsync("pv-staff-2");
        var (journeyId, stops) = await api.JourneyWithStopsAsync(3);
        await api.GrantAsync(staff, Roles.StationStaff, stops[1].StationId);
        var ownChange = await api.SeedChangeAsync(journeyId, 1, controller, ChangeDetails.Platform(stops[1].StopId, "5"));
        var otherChange = await api.SeedChangeAsync(journeyId, 2, controller, ChangeDetails.Platform(stops[2].StopId, "6"));
        var delayChange = await api.SeedChangeAsync(journeyId, 3, controller, ChangeDetails.Delay(10));

        Assert.Equal(HttpStatusCode.OK, (await api.PreviewAsync("pv-staff-2", journeyId, ChangeBodies.Withdraw(ownChange))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.PreviewAsync("pv-staff-2", journeyId, ChangeBodies.Withdraw(otherChange))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.PreviewAsync("pv-staff-2", journeyId, ChangeBodies.Withdraw(delayChange))).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task A_guest_gets_401()
    {
        var (journeyId, _) = await api.JourneyWithStopsAsync(0);

        var response = await api.PreviewAsync(null, journeyId, ChangeBodies.Delay(5));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task A_valid_token_without_a_local_account_or_with_a_disabled_one_gets_403()
    {
        await api.AddAccountAsync("pv-disabled", status: AccountStatus.Disabled, roles: Roles.Controller);
        var (journeyId, _) = await api.JourneyWithStopsAsync(0);

        Assert.Equal(HttpStatusCode.Forbidden, (await api.PreviewAsync("pv-disabled", journeyId, ChangeBodies.Delay(5))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.PreviewAsync("pv-nobody", journeyId, ChangeBodies.Delay(5))).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task An_expired_grant_gets_403_and_is_audited()
    {
        var expired = await api.AddAccountAsync("pv-expired");
        var (journeyId, stops) = await api.JourneyWithStopsAsync(0);
        await api.GrantAsync(expired, Roles.StationStaff, stops[1].StationId, DateTimeOffset.UtcNow.AddMinutes(-1));

        var response = await api.PreviewAsync("pv-expired", journeyId, ChangeBodies.Platform(stops[1].StopId, "5"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(await api.AuditAsync(expired), e => e.Outcome == "DENIED" && e.TargetId == journeyId);
    }

    [RequiresDatabaseTheory]
    [InlineData(Roles.SysAdmin)]
    [InlineData(Roles.Auditor)]
    [InlineData(Roles.Passenger)]
    [InlineData(Roles.Driver)]
    [InlineData(Roles.SimOperator)]
    public async Task Roles_that_do_not_publish_get_403_and_the_attempt_is_audited(string role)
    {
        var subject = $"pv-role-{role}";
        var account = await api.AddAccountAsync(subject, roles: role);
        var (journeyId, _) = await api.JourneyWithStopsAsync(0);

        var response = await api.PreviewAsync(subject, journeyId, ChangeBodies.Delay(5));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var denied = Assert.Single(await api.AuditAsync(account));
        Assert.Equal(("DENIED", "JOURNEY", (Guid?)journeyId), (denied.Outcome, denied.TargetType, denied.TargetId));
        Assert.Equal((0, 0), await api.CountsAsync(journeyId));
    }

    [RequiresDatabaseFact]
    public async Task Bad_requests_are_400_and_unknown_things_are_404()
    {
        var controller = await api.AddAccountAsync("pv-controller-5", roles: Roles.Controller);
        var (journeyId, stops) = await api.JourneyWithStopsAsync(0);
        var withdrawn = await api.SeedChangeAsync(journeyId, 1, controller, ChangeDetails.Delay(10));
        await api.SeedChangeAsync(journeyId, 2, controller, new ChangeDetails(ChangeKind.Delay), ChangeAction.Withdraw, withdrawn);

        Task<HttpResponseMessage> Preview(object? body, Guid? journey = null) =>
            api.PreviewAsync("pv-controller-5", journey ?? journeyId, body);

        Assert.Equal(HttpStatusCode.NotFound, (await Preview(ChangeBodies.Delay(5), Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Preview(null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Preview(new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Preview(new { draft = new { kind = "DELAY", delayMinutes = 5 }, withdrawChangeId = withdrawn })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Preview(ChangeBodies.Delay(-1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Preview(new { draft = new { kind = "CANCEL", delayMinutes = 5 } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Preview(ChangeBodies.Platform(stops[0].StationId, "5"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Preview(ChangeBodies.Withdraw(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Preview(ChangeBodies.Withdraw(withdrawn))).StatusCode);
    }
}
