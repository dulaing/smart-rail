using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
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

/// <summary>
/// The API with a test token signer standing in for Keycloak: the API validates tokens the
/// same way, but trusts this fixture's key instead of fetching Keycloak's.
/// </summary>
public sealed class AccessApi : TimetableApi
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
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration)));
    }

    public sealed record TokenOptions
    {
        public string Issuer { get; init; } = AccessApi.Issuer;
        public string Audience { get; init; } = AccessApi.Audience;
        public string Type { get; init; } = "Bearer";
        /// <summary>Negative for an already expired token; must stay after 10 minutes ago.</summary>
        public TimeSpan ExpiresIn { get; init; } = TimeSpan.FromMinutes(5);
        public bool SignedByAnotherKey { get; init; }
    }

    /// <summary>A Keycloak-shaped access token for <paramref name="subject"/>.</summary>
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

    /// <summary>Adds a local account and returns its ID. Grants are given as role codes (no station).</summary>
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

    public override async Task DisposeAsync()
    {
        _signingRsa.Dispose();
        _otherRsa.Dispose();
        await base.DisposeAsync();
    }
}
