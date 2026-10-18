using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Access;

// The API with a test token signer standing in for Keycloak: the API validates tokens the
// same way, but trusts this fixture's key instead of fetching Keycloak's.
public class AccessApi : TimetableApi
{
    public const string Issuer = "https://keycloak.test/realms/smartrail";
    public const string Audience = "smartrail-api";

    private readonly RSA _signingRsa = RSA.Create(2048);
    private readonly RSA _otherRsa = RSA.Create(2048);

    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Identity:Issuer", Issuer);
        builder.UseSetting("Identity:Audience", Audience);

        var publicKey = new RsaSecurityKey(_signingRsa.ExportParameters(false)) { KeyId = "test-key" };
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(publicKey);

        builder.ConfigureTestServices(services =>
        {
            services.AddTransient<IStartupFilter, TestOperationsFilter>();
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration));
        });
    }

    // Stand-ins for the operational endpoints that do not exist yet. They are mapped only in
    // this test host and use the same guard and policies that real endpoints will use.
    private sealed class TestOperationsFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // A separate branch with its own routing, but the same services, token validation and
            // policies as the real pipeline, so these behave like production endpoints.
            app.Map("/test-ops", branch =>
            {
                branch.UseRouting();
                branch.UseAuthentication();
                branch.UseAuthorization();
                branch.UseEndpoints(endpoints =>
                {
                    endpoints.MapPost("/changes", () => Results.Ok())
                        .RequireAuthorization(AccessPolicies.For(Permissions.ChangesPublish));

                    endpoints.MapPost("/stations/{stationId:guid}/platform-change",
                        async (Guid stationId, HttpContext context, AccessGuard guard) =>
                            await guard.CheckAsync(context, Permissions.PlatformChangesPublish, new AccessTarget.Station(stationId))
                                == AccessDecision.Allowed ? Results.Ok() : Results.Forbid())
                        .RequireAuthorization(AccessPolicies.ActiveAccount);

                    endpoints.MapPost("/journeys/{journeyId:guid}/report",
                        async (Guid journeyId, HttpContext context, AccessGuard guard) =>
                            await guard.CheckAsync(context, Permissions.DriverReportsCreate, new AccessTarget.Journey(journeyId))
                                == AccessDecision.Allowed ? Results.Ok() : Results.Forbid())
                        .RequireAuthorization(AccessPolicies.ActiveAccount);
                });
            });
            next(app);
        };
    }

    public sealed record TokenOptions
    {
        public string Issuer { get; init; } = AccessApi.Issuer;
        public string Audience { get; init; } = AccessApi.Audience;
        public string Type { get; init; } = "Bearer";
        // Negative for an already expired token; must stay after 10 minutes ago.
        public TimeSpan ExpiresIn { get; init; } = TimeSpan.FromMinutes(5);
        public bool SignedByAnotherKey { get; init; }
    }

    // A Keycloak-shaped access token for subject.
    public string Token(string subject, TokenOptions? options = null)
    {
        options ??= new TokenOptions();
        var now = DateTime.UtcNow;
        var key = new RsaSecurityKey(options.SignedByAnotherKey ? _otherRsa : _signingRsa) { KeyId = "test-key" };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            NotBefore = now.AddMinutes(-10),
            Expires = now.Add(options.ExpiresIn),
            Claims = new Dictionary<string, object> { ["sub"] = subject, ["typ"] = options.Type },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public HttpClient ClientFor(string? token)
    {
        var client = Factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    // Adds a local account and returns its ID. Grants are given as role codes (no station).
    public async Task<Guid> AddAccountAsync(
        string subject, string displayName = "Test Person", AccountStatus status = AccountStatus.Active,
        string issuer = Issuer, params string[] roles)
    {
        var id = Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            db.Accounts.Add(new Account { Id = id, Issuer = issuer, Subject = subject, DisplayName = displayName, Status = status });
            await db.SaveChangesAsync();
            foreach (var code in roles)
            {
                var role = await db.Roles.SingleAsync(r => r.Code == code);
                db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), AccountId = id, RoleId = role.Id });
            }

            await db.SaveChangesAsync();
        });
        return id;
    }

    public async Task<Guid> StationIdAsync(string code)
    {
        var id = Guid.Empty;
        await WithDbAsync(async db => id = await db.Stations.Where(s => s.Code == code).Select(s => s.Id).SingleAsync());
        return id;
    }

    public async Task<Guid> JourneyIdAsync(int skip = 0)
    {
        var id = Guid.Empty;
        await WithDbAsync(async db =>
            id = await db.Journeys.OrderBy(j => j.DemoSeedKey).Skip(skip).Select(j => j.Id).FirstAsync());
        return id;
    }

    public Task GrantAsync(Guid accountId, string role, Guid? stationId = null, DateTimeOffset? validUntil = null) =>
        WithDbAsync(async db =>
        {
            var roleId = await db.Roles.Where(r => r.Code == role).Select(r => r.Id).SingleAsync();
            db.RoleGrants.Add(new RoleGrant
            {
                Id = Guid.NewGuid(), AccountId = accountId, RoleId = roleId,
                StationScopeId = stationId, ValidUntil = validUntil,
            });
            await db.SaveChangesAsync();
        });

    public Task AssignAsync(Guid driverId, Guid journeyId) =>
        WithDbAsync(async db =>
        {
            db.JourneyAssignments.Add(new JourneyAssignment { Id = Guid.NewGuid(), DriverId = driverId, JourneyId = journeyId });
            await db.SaveChangesAsync();
        });

    public async Task<List<AuditEntry>> AuditAsync(Guid actorId)
    {
        var entries = new List<AuditEntry>();
        await WithDbAsync(async db =>
            entries = await db.AuditEntries.AsNoTracking().Where(e => e.ActorId == actorId).OrderBy(e => e.OccurredAt).ToListAsync());
        return entries;
    }

    public override async Task DisposeAsync()
    {
        _signingRsa.Dispose();
        _otherRsa.Dispose();
        await base.DisposeAsync();
    }
}
