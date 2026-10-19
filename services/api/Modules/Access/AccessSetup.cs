using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace SmartRail.Api.Modules.Access;

public static class AccessPolicies
{
    public const string ActiveAccount = "ActiveAccount";

    // The policy that requires a network-wide permission, such as accounts.manage.
    public static string For(string permission) => $"permission:{permission}";
}

public static class AccessSetup
{
    // Keycloak access tokens carry typ "Bearer"; ID and refresh tokens carry another type. The
    // audience check already rejects an ID token, and this makes the rule explicit.
    private const string AccessTokenType = "Bearer";

    // Validates Keycloak access tokens (issuer, signature, audience, expiry) and makes every
    // endpoint require a valid token and an enabled local account unless it opts out with
    // AllowAnonymous. SmartRail issues no tokens.
    public static void AddSmartRailAccess(this WebApplicationBuilder builder, bool hasDatabase)
    {
        var issuer = builder.Configuration["Identity:Issuer"];
        var audience = builder.Configuration["Identity:Audience"];

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // The issuer is both the expected "iss" and where the signing keys are found.
                options.Authority = issuer;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.FindFirst("typ")?.Value != AccessTokenType)
                        {
                            context.Fail("Not an access token.");
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        var activeAccount = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new ActiveAccountRequirement())
            .Build();

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AccessPolicies.ActiveAccount, activeAccount);
            foreach (var (code, scope) in Permissions.Scopes.Where(s => s.Value == ScopeKind.Network))
            {
                options.AddPolicy(AccessPolicies.For(code), policy => policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new ActiveAccountRequirement(), new PermissionRequirement(code)));
            }

            // Deny by default: an endpoint with no authorization metadata needs a signed-in,
            // enabled account. Public routes say so with AllowAnonymous.
            options.FallbackPolicy = activeAccount;
        });

        // Local accounts live in the database. Without one no protected route is mapped, and
        // the missing handler would deny anyway.
        if (hasDatabase)
        {
            builder.Services.AddScoped<AccountDirectory>();
            builder.Services.AddScoped<CurrentAccount>();
            builder.Services.AddScoped<IAuthorizationHandler, ActiveAccountHandler>();
            builder.Services.AddScoped<AccessGuard>();
            builder.Services.AddScoped<IAuditTrail, AuditTrail>();
            builder.Services.AddScoped<IAccountNames, AccountNames>();
            builder.Services.AddScoped<IDriverAssignments, DriverAssignments>();
            builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        }
    }
}
