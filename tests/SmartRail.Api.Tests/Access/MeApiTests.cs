using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Access;

// Token validation and account resolution against a disposable PostgreSQL. The test signer
// stands in for Keycloak, so no identity server has to be running.
public class MeApiTests(AccessApi api) : IClassFixture<AccessApi>
{
    private async Task<HttpResponseMessage> GetMeAsync(string? token)
    {
        using var client = api.ClientFor(token);
        return await client.GetAsync("/api/me");
    }

    [RequiresDatabaseFact]
    public async Task Missing_token_is_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMeAsync(null)).StatusCode);
    }

    [RequiresDatabaseTheory]
    [InlineData("garbage")]
    [InlineData("expired")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-signing-key")]
    [InlineData("id-token")]
    public async Task Invalid_token_is_401_even_for_a_known_account(string kind)
    {
        await api.AddAccountAsync($"subject-{kind}", roles: Roles.Passenger);
        var subject = $"subject-{kind}";
        var token = kind switch
        {
            "garbage" => "not.a.jwt",
            "expired" => api.Token(subject, new AccessApi.TokenOptions { ExpiresIn = TimeSpan.FromMinutes(-5) }),
            "wrong-issuer" => api.Token(subject, new AccessApi.TokenOptions { Issuer = "https://evil.test/realms/smartrail" }),
            "wrong-audience" => api.Token(subject, new AccessApi.TokenOptions { Audience = "another-api" }),
            "wrong-signing-key" => api.Token(subject, new AccessApi.TokenOptions { SignedByAnotherKey = true }),
            // An ID token is addressed to the client and typed "ID"; here it even names our audience.
            "id-token" => api.Token(subject, new AccessApi.TokenOptions { Type = "ID" }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMeAsync(token)).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task Valid_token_for_an_unknown_subject_is_403()
    {
        var response = await GetMeAsync(api.Token("nobody-has-this-subject"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task Valid_token_for_a_disabled_account_is_403()
    {
        await api.AddAccountAsync("disabled-person", status: AccountStatus.Disabled, roles: Roles.Controller);

        var response = await GetMeAsync(api.Token("disabled-person"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task The_same_subject_under_another_issuer_is_a_different_identity()
    {
        await api.AddAccountAsync("shared-subject", issuer: "https://other-idp.test/realms/x", roles: Roles.Controller);

        var response = await GetMeAsync(api.Token("shared-subject"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task Valid_token_returns_the_matching_local_account_with_its_effective_permissions()
    {
        var id = await api.AddAccountAsync("valid-controller", "Dilani Perera", roles: Roles.Controller);
        await api.AddAccountAsync("someone-else", "Someone Else", roles: Roles.Passenger);

        var response = await GetMeAsync(api.Token("valid-controller"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, body.GetProperty("accountId").GetGuid());
        Assert.Equal("Dilani Perera", body.GetProperty("displayName").GetString());
        Assert.Equal("ACTIVE", body.GetProperty("status").GetString());
        Assert.Equal([Roles.Controller], body.GetProperty("grants").EnumerateArray()
            .Select(g => g.GetProperty("role").GetString()).ToList());
        var permissions = body.GetProperty("permissions").EnumerateArray()
            .ToDictionary(p => p.GetProperty("code").GetString()!, p => p.GetProperty("scope").GetString());
        Assert.Equal("NETWORK", permissions[Permissions.ChangesPublish]);
        Assert.False(permissions.ContainsKey(Permissions.AccountsManage));
    }

    [RequiresDatabaseFact]
    public async Task Keycloak_role_claims_do_not_grant_anything()
    {
        // The token says "SYS_ADMIN" but the local account holds only a passenger grant.
        await api.AddAccountAsync("claims-admin", roles: Roles.Passenger);

        var response = await GetMeAsync(api.Token("claims-admin"));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var codes = body.GetProperty("permissions").EnumerateArray().Select(p => p.GetProperty("code").GetString());
        Assert.DoesNotContain(Permissions.AccountsManage, codes);
    }

    [RequiresDatabaseFact]
    public async Task Public_timetable_and_health_routes_work_without_login_or_with_a_bad_token()
    {
        var results = new List<string>();
        foreach (var token in new string?[] { null, "not.a.jwt" })
        {
            using var client = api.ClientFor(token);
            foreach (var path in new[] { "/api/stations", "/api/journeys?serviceDate=2026-10-14", "/health/live", "/health/ready" })
            {
                var response = await client.GetAsync(path);
                results.Add($"{token ?? "none"} {path} {(int)response.StatusCode}");
            }
        }

        Assert.All(results, r => Assert.EndsWith(" 200", r));
    }

    [RequiresDatabaseFact]
    public void Only_the_intended_routes_are_anonymous()
    {
        var anonymous = api.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText)
            .Order()
            .ToList();

        // The internal observation and simulator routes skip the signed-in-user rule because a
        // service key guards them instead; the Observation and Simulator tests prove each one
        // refuses everything without the key.
        Assert.Equal(
            [
                "/api/journeys", "/api/journeys/{journeyId}/running", "/api/next-trains", "/api/stations", "/health/live", "/health/ready",
                "/internal/rail/observations",
                "/internal/rail/simulator/journeys/{journeyId}/pause", "/internal/rail/simulator/journeys/{journeyId}/resume",
                "/openapi/{documentName}.json",
            ],
            anonymous);
    }

    [RequiresDatabaseFact]
    public async Task Role_matrix_is_seeded_as_permission_rows_and_reseeding_changes_nothing()
    {
        async Task<List<string>> RowsAsync()
        {
            var rows = new List<string>();
            await api.WithDbAsync(async db =>
            {
                var roles = await db.Roles.Include(r => r.Permissions).ToListAsync();
                rows = roles.SelectMany(r => r.Permissions.Select(p => $"{r.Code}:{p.PermissionCode}")).Order().ToList();
            });
            return rows;
        }

        var first = await RowsAsync();
        await api.MigrateAndSeedAsync();
        var second = await RowsAsync();

        var expected = Roles.All
            .SelectMany(role => RoleMatrix.PermissionsOf(role).Select(p => $"{role}:{p}"))
            .Order()
            .ToList();
        Assert.Equal(expected, first);
        Assert.Equal(first, second);
    }
}
