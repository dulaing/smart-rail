using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Demo;
using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Tests.Catering;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Demo;

// The staff demo adapter: who may use it (SimOperator or an explicit grant, nobody else), that it
// is Development-only and honest when the simulator is off, that unknown journeys are refused, and
// that no provider or internal secret ever reaches a response.
public class DemoControlsApiTests(DemoApi api) : IClassFixture<DemoApi>
{
    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

    private async Task<HttpResponseMessage> SendAsync(string subject, HttpMethod method, string path, object? body = null)
    {
        using var client = api.ClientFor(api.Token(subject));
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = Json(body);
        }

        return await client.SendAsync(request);
    }

    private static string SimulationPath(Guid journeyId) => $"/api/demo/journeys/{journeyId}/simulation";

    [RequiresDatabaseFact]
    public async Task A_simulator_operator_can_read_and_assign_a_journeys_simulation_state()
    {
        var operatorId = await api.AddAccountAsync("demo-sim-op", "Demo Operator", roles: Roles.SimOperator);
        var journey = await api.JourneyIdAsync(0);
        var control = api.Factory.Services.GetRequiredService<SimulatorControl>();

        var read = await SendAsync("demo-sim-op", HttpMethod.Get, SimulationPath(journey));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var state = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(journey, state.GetProperty("journeyId").GetGuid());
        Assert.True(state.GetProperty("enabled").GetBoolean());
        Assert.False(state.GetProperty("paused").GetBoolean());

        try
        {
            var pause = await SendAsync("demo-sim-op", HttpMethod.Post, $"/api/demo/journeys/{journey}/pause");
            Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
            Assert.True((await pause.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paused").GetBoolean());
            Assert.True(control.IsPaused(journey));

            // The desired state was assigned, not toggled: asking again changes nothing and still succeeds.
            var again = await SendAsync("demo-sim-op", HttpMethod.Post, $"/api/demo/journeys/{journey}/pause");
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            Assert.True(control.IsPaused(journey));
        }
        finally
        {
            var resume = await SendAsync("demo-sim-op", HttpMethod.Post, $"/api/demo/journeys/{journey}/resume");
            Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
            Assert.False(control.IsPaused(journey));
        }

        var audit = await api.AuditAsync(operatorId);
        Assert.Equal(
            [(DemoSetup.PauseAction, "SUCCEEDED", "JOURNEY"), (DemoSetup.PauseAction, "SUCCEEDED", "JOURNEY"),
             (DemoSetup.ResumeAction, "SUCCEEDED", "JOURNEY")],
            audit.Select(a => (a.Action, a.Outcome, a.TargetType)));
        Assert.DoesNotContain(audit, a => a.CorrelationId.Length == 0);
    }

    [RequiresDatabaseFact]
    public async Task A_second_account_needs_an_explicit_grant_of_the_permission()
    {
        var journey = await api.JourneyIdAsync(0);
        var accountId = await api.AddAccountAsync("demo-granted", "Granted Controller", roles: Roles.Controller);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync("demo-granted", HttpMethod.Get, SimulationPath(journey))).StatusCode);

        // The explicit grant of the permission's own role is what opens it; the account is unchanged.
        await api.GrantAsync(accountId, Roles.SimOperator);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync("demo-granted", HttpMethod.Get, SimulationPath(journey))).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task Guests_and_ungranted_staff_are_denied_and_the_denial_is_audited()
    {
        var journey = await api.JourneyIdAsync(0);

        using var guest = api.ClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync(SimulationPath(journey))).StatusCode);

        var controllerId = await api.AddAccountAsync("demo-controller", roles: Roles.Controller);
        var sysAdminId = await api.AddAccountAsync("demo-sysadmin", roles: Roles.SysAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync("demo-controller", HttpMethod.Get, SimulationPath(journey))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync("demo-sysadmin", HttpMethod.Get, SimulationPath(journey))).StatusCode);

        foreach (var actor in new[] { controllerId, sysAdminId })
        {
            var denied = Assert.Single(await api.AuditAsync(actor));
            Assert.Equal((Permissions.SimulationControl, "DENIED", "NETWORK"), (denied.Action, denied.Outcome, denied.TargetType));
        }
    }

    [RequiresDatabaseFact]
    public async Task Revoked_and_expired_simulation_grants_are_denied()
    {
        var journey = await api.JourneyIdAsync(0);

        var expiredId = await api.AddAccountAsync("demo-expired", roles: Roles.Passenger);
        await api.GrantAsync(expiredId, Roles.SimOperator, validUntil: api.Clock.Now.AddMinutes(-1));
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync("demo-expired", HttpMethod.Get, SimulationPath(journey))).StatusCode);

        var revokedId = await api.AddAccountAsync("demo-revoked", roles: Roles.SimOperator);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync("demo-revoked", HttpMethod.Get, SimulationPath(journey))).StatusCode);
        await api.WithDbAsync(async db =>
        {
            db.RoleGrants.RemoveRange(db.RoleGrants.Where(g => g.AccountId == revokedId));
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync("demo-revoked", HttpMethod.Get, SimulationPath(journey))).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task A_disabled_simulator_refuses_the_actions_but_still_reports_state()
    {
        await api.AddAccountAsync("demo-off", roles: Roles.SimOperator);
        var journey = await api.JourneyIdAsync(0);

        using var host = api.Factory.WithWebHostBuilder(builder => builder.UseSetting(SimulatorSetup.EnabledSetting, "false"));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Token("demo-off"));

        var read = await client.GetAsync(SimulationPath(journey));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var state = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(state.GetProperty("enabled").GetBoolean());
        Assert.False(state.GetProperty("paused").GetBoolean());

        var pause = await client.PostAsync($"/api/demo/journeys/{journey}/pause", null);
        Assert.Equal(HttpStatusCode.Conflict, pause.StatusCode);
        Assert.Equal("SIMULATOR_DISABLED", (await pause.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        // The refusal did not silently change the pause list: state is unchanged.
        Assert.False((await (await client.GetAsync(SimulationPath(journey))).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("paused").GetBoolean());
    }

    [RequiresDatabaseFact]
    public async Task Unknown_journeys_are_rejected()
    {
        await api.AddAccountAsync("demo-unknown", roles: Roles.SimOperator);
        var missing = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync("demo-unknown", HttpMethod.Get, SimulationPath(missing))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync("demo-unknown", HttpMethod.Post, $"/api/demo/journeys/{missing}/pause")).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task The_demo_routes_do_not_exist_outside_Development()
    {
        using var production = api.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var routes = production.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>().Select(e => e.RoutePattern.RawText).ToList();

        Assert.DoesNotContain(routes, r => r!.Contains("/api/demo"));
        Assert.Contains(routes, r => r == "/api/journeys/{journeyId}/running");   // other routes are unaffected
        Assert.False((await production.CreateClient().GetAsync("/api/demo/diagnostics")).IsSuccessStatusCode);
    }

    [RequiresDatabaseFact]
    public async Task Responses_carry_no_provider_or_internal_secret()
    {
        await api.AddAccountAsync("demo-secret", roles: Roles.SimOperator);
        var journey = await api.JourneyIdAsync(0);

        var pause = await SendAsync("demo-secret", HttpMethod.Post, $"/api/demo/journeys/{journey}/pause");
        var read = await SendAsync("demo-secret", HttpMethod.Get, SimulationPath(journey));
        var diagnostics = await SendAsync("demo-secret", HttpMethod.Get, "/api/demo/diagnostics");
        var outage = await SendAsync("demo-secret", HttpMethod.Get, "/api/demo/provider/outage");
        await SendAsync("demo-secret", HttpMethod.Post, $"/api/demo/journeys/{journey}/resume");

        foreach (var response in new[] { pause, read, diagnostics, outage })
        {
            var text = await response.Content.ReadAsStringAsync();
            foreach (var secret in new[]
            {
                CateringApi.ObservationKey, RailBitesProcess.ServiceKey, RailBitesProcess.CallbackSecret,
                RailBitesProcess.AdminKey, RailBitesProcess.PolgahawelaKioskKey,
            })
            {
                Assert.DoesNotContain(secret, text);
            }
        }
    }

    [RequiresDatabaseFact]
    public async Task The_outage_switch_sets_and_ends_a_simulated_outage()
    {
        var operatorId = await api.AddAccountAsync("demo-outage", roles: Roles.SimOperator);

        var initial = await SendAsync("demo-outage", HttpMethod.Get, "/api/demo/provider/outage");
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.False((await initial.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("active").GetBoolean());

        try
        {
            var on = await SendAsync("demo-outage", HttpMethod.Put, "/api/demo/provider/outage", new { active = true });
            Assert.Equal(HttpStatusCode.OK, on.StatusCode);
            Assert.True((await on.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("active").GetBoolean());

            // The provider really behaves as down, and the switch's own route stays reachable.
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await api.RailBites.Http.GetAsync("/v1/collection-points")).StatusCode);

            // Setting the same desired state again is idempotent and still succeeds.
            var repeat = await SendAsync("demo-outage", HttpMethod.Put, "/api/demo/provider/outage", new { active = true });
            Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);

            var off = await SendAsync("demo-outage", HttpMethod.Put, "/api/demo/provider/outage", new { active = false });
            Assert.Equal(HttpStatusCode.OK, off.StatusCode);
            Assert.False((await off.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("active").GetBoolean());
            Assert.Equal(HttpStatusCode.OK, (await api.RailBites.Http.GetAsync("/v1/collection-points")).StatusCode);
        }
        finally
        {
            await api.RailBites.Http.PutAsJsonAsync("/internal/dev/outage", new { active = false });
        }

        var audit = await api.AuditAsync(operatorId);
        Assert.Equal([DemoSetup.OutageAction, DemoSetup.OutageAction, DemoSetup.OutageAction], audit.Select(a => a.Action));
        Assert.All(audit, a => Assert.Equal("SUCCEEDED", a.Outcome));
    }

    [RequiresDatabaseFact]
    public async Task Diagnostics_reuse_the_readiness_backlog_numbers()
    {
        await api.AddAccountAsync("demo-diagnostics", roles: Roles.SimOperator);

        var response = await SendAsync("demo-diagnostics", HttpMethod.Get, "/api/demo/diagnostics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        var data = body.GetProperty("data");
        Assert.Equal("Ready", data.GetProperty("provider").GetString());
        Assert.Equal(0, data.GetProperty("outboxPending").GetInt32());
        Assert.Equal(0, data.GetProperty("oldestOutboxSeconds").GetInt32());
        Assert.Equal(0, data.GetProperty("ordersPending").GetInt32());
        Assert.Equal(0, data.GetProperty("oldestPendingOrderSeconds").GetInt32());
    }
}
