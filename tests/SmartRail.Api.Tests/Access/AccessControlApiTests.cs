using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Access;

/// <summary>
/// Grant and status changes, scope limits and audit rows against a disposable PostgreSQL.
/// Operational routes are test-only stand-ins using the production guard and policies.
/// </summary>
public class AccessControlApiTests(AccessApi api) : IClassFixture<AccessApi>
{
    private HttpClient As(string subject) => api.ClientFor(api.Token(subject));

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

    private async Task<HttpStatusCode> PostAsync(string subject, string path)
    {
        using var client = As(subject);
        return (await client.PostAsync(path, null)).StatusCode;
    }

    [RequiresDatabaseFact]
    public async Task Disabling_an_account_denies_a_still_valid_token_immediately_and_is_audited()
    {
        var admin = await api.AddAccountAsync("admin-1", roles: Roles.SysAdmin);
        var target = await api.AddAccountAsync("controller-1", roles: Roles.Controller);
        Assert.Equal(HttpStatusCode.OK, await PostAsync("controller-1", "/test-ops/changes"));

        using var client = As("admin-1");
        var disable = await client.PutAsync($"/api/admin/accounts/{target}/status", Json(new { status = "DISABLED" }));
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync("controller-1", "/test-ops/changes"));
        using var me = As("controller-1");
        Assert.Equal(HttpStatusCode.Forbidden, (await me.GetAsync("/api/me")).StatusCode);

        var audit = Assert.Single(await api.AuditAsync(admin));
        Assert.Equal((AccessAdminEndpoints.SetStatusAction, "SUCCEEDED", "ACCOUNT", (Guid?)target),
            (audit.Action, audit.Outcome, audit.TargetType, audit.TargetId));

        // Enabling again restores access; repeating the same status adds no second audit row.
        await client.PutAsync($"/api/admin/accounts/{target}/status", Json(new { status = "ACTIVE" }));
        await client.PutAsync($"/api/admin/accounts/{target}/status", Json(new { status = "ACTIVE" }));
        Assert.Equal(HttpStatusCode.OK, await PostAsync("controller-1", "/test-ops/changes"));
        Assert.Equal(2, (await api.AuditAsync(admin)).Count);
    }

    [RequiresDatabaseFact]
    public async Task Removing_a_grant_denies_access_immediately_and_is_audited()
    {
        var admin = await api.AddAccountAsync("admin-2", roles: Roles.SysAdmin);
        var target = await api.AddAccountAsync("controller-2", roles: Roles.Controller);
        Assert.Equal(HttpStatusCode.OK, await PostAsync("controller-2", "/test-ops/changes"));

        using var client = As("admin-2");
        var grants = await (await client.GetAsync($"/api/admin/accounts/{target}/grants")).Content.ReadFromJsonAsync<JsonElement>();
        var grantId = grants[0].GetProperty("grantId").GetGuid();

        var remove = await client.DeleteAsync($"/api/admin/accounts/{target}/grants/{grantId}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync("controller-2", "/test-ops/changes"));
        var audit = Assert.Single(await api.AuditAsync(admin));
        Assert.Equal((AccessAdminEndpoints.RemoveGrantAction, "SUCCEEDED", "GRANT", (Guid?)grantId),
            (audit.Action, audit.Outcome, audit.TargetType, audit.TargetId));
    }

    [RequiresDatabaseFact]
    public async Task A_new_grant_takes_effect_on_the_next_request_and_a_repeat_updates_it()
    {
        var admin = await api.AddAccountAsync("admin-3", roles: Roles.SysAdmin);
        var target = await api.AddAccountAsync("passenger-3", roles: Roles.Passenger);
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync("passenger-3", "/test-ops/changes"));

        using var client = As("admin-3");
        var first = await client.PostAsync($"/api/admin/accounts/{target}/grants", Json(new { role = Roles.Controller }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await PostAsync("passenger-3", "/test-ops/changes"));

        var until = DateTimeOffset.UtcNow.AddDays(1);
        var second = await client.PostAsync($"/api/admin/accounts/{target}/grants", Json(new { role = Roles.Controller, validUntil = until }));
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("grantId").GetGuid();
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("grantId").GetGuid();
        Assert.Equal(firstId, secondId);
        Assert.Equal(2, (await api.AuditAsync(admin)).Count);
    }

    [RequiresDatabaseTheory]
    [InlineData("unknown-role")]
    [InlineData("station-staff-without-station")]
    [InlineData("controller-with-station")]
    [InlineData("unknown-station")]
    [InlineData("past-end-date")]
    public async Task Invalid_grants_are_rejected_and_not_audited(string kind)
    {
        var admin = await api.AddAccountAsync($"admin-v-{kind}", roles: Roles.SysAdmin);
        var target = await api.AddAccountAsync($"target-v-{kind}", roles: Roles.Passenger);
        var station = await api.StationIdAsync("GPH");
        object body = kind switch
        {
            "unknown-role" => new { role = "ROOT" },
            "station-staff-without-station" => new { role = Roles.StationStaff },
            "controller-with-station" => new { role = Roles.Controller, stationId = station },
            "unknown-station" => new { role = Roles.StationStaff, stationId = Guid.NewGuid() },
            _ => new { role = Roles.Controller, validUntil = DateTimeOffset.UtcNow.AddMinutes(-1) },
        };

        using var client = As($"admin-v-{kind}");
        var response = await client.PostAsync($"/api/admin/accounts/{target}/grants", Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await api.AuditAsync(admin));
    }

    [RequiresDatabaseTheory]
    [InlineData(Roles.Controller)]
    [InlineData(Roles.Auditor)]
    [InlineData(Roles.Passenger)]
    public async Task Only_a_SysAdmin_may_manage_accounts_and_the_denial_is_audited(string role)
    {
        var actor = await api.AddAccountAsync($"not-admin-{role}", roles: role);
        var target = await api.AddAccountAsync($"victim-{role}", roles: Roles.Controller);

        using var client = As($"not-admin-{role}");
        var status = await client.PutAsync($"/api/admin/accounts/{target}/status", Json(new { status = "DISABLED" }));
        var grants = await client.PostAsync($"/api/admin/accounts/{target}/grants", Json(new { role = Roles.SysAdmin }));

        Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, grants.StatusCode);
        var audit = await api.AuditAsync(actor);
        Assert.Equal(
            [(Permissions.AccountsManage, "DENIED", "NETWORK"), (Permissions.GrantsManage, "DENIED", "NETWORK")],
            audit.Select(a => (a.Action, a.Outcome, a.TargetType)));
    }

    [RequiresDatabaseFact]
    public async Task Admin_routes_need_a_token_and_an_enabled_account()
    {
        var anonymous = api.ClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsync($"/api/admin/accounts/{Guid.NewGuid()}/status", Json(new { status = "ACTIVE" }))).StatusCode);

        using var stranger = As("no-local-account");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await stranger.PutAsync($"/api/admin/accounts/{Guid.NewGuid()}/status", Json(new { status = "ACTIVE" }))).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task A_SysAdmin_cannot_change_their_own_account_or_make_operational_edits()
    {
        var admin = await api.AddAccountAsync("admin-self", roles: Roles.SysAdmin);
        var station = await api.StationIdAsync("GPH");

        using var client = As("admin-self");
        var self = await client.PutAsync($"/api/admin/accounts/{admin}/status", Json(new { status = "DISABLED" }));
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync("admin-self", "/test-ops/changes"));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync("admin-self", $"/test-ops/stations/{station}/platform-change"));

        var audit = await api.AuditAsync(admin);
        Assert.Equal([Permissions.ChangesPublish, Permissions.PlatformChangesPublish], audit.Select(a => a.Action));
        Assert.All(audit, a => Assert.Equal("DENIED", a.Outcome));
    }

    [RequiresDatabaseFact]
    public async Task Station_staff_can_act_only_at_their_own_station()
    {
        var staff = await api.AddAccountAsync("staff-gph");
        var gampaha = await api.StationIdAsync("GPH");
        var ragama = await api.StationIdAsync("RGM");
        await api.GrantAsync(staff, Roles.StationStaff, gampaha);

        Assert.Equal(HttpStatusCode.OK, await PostAsync("staff-gph", $"/test-ops/stations/{gampaha}/platform-change"));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync("staff-gph", $"/test-ops/stations/{ragama}/platform-change"));

        var denied = Assert.Single(await api.AuditAsync(staff));
        Assert.Equal((Permissions.PlatformChangesPublish, "DENIED", "STATION", (Guid?)ragama),
            (denied.Action, denied.Outcome, denied.TargetType, denied.TargetId));
    }

    [RequiresDatabaseFact]
    public async Task A_driver_can_act_only_on_an_assigned_journey()
    {
        var driver = await api.AddAccountAsync("driver-1", roles: Roles.Driver);
        var assigned = await api.JourneyIdAsync(0);
        var other = await api.JourneyIdAsync(1);
        await api.AssignAsync(driver, assigned);

        Assert.Equal(HttpStatusCode.OK, await PostAsync("driver-1", $"/test-ops/journeys/{assigned}/report"));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync("driver-1", $"/test-ops/journeys/{other}/report"));

        var denied = Assert.Single(await api.AuditAsync(driver));
        Assert.Equal(("JOURNEY", (Guid?)other), (denied.TargetType, denied.TargetId));
    }

    [RequiresDatabaseFact]
    public async Task An_expired_grant_is_denied_without_any_admin_action()
    {
        var expired = await api.AddAccountAsync("expired-controller");
        await api.GrantAsync(expired, Roles.Controller, validUntil: DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync("expired-controller", "/test-ops/changes"));
    }
}
