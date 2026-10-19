using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Access;

// Development only: creates a few synthetic Keycloak users and the local accounts that map to
// them. Keycloak chooses each user's subject, so the local account is linked to the subject
// Keycloak actually returns. Run it again to reset the demo passwords; nothing is duplicated.
// Credentials come from configuration (user-secrets or environment), never from source.
public static class DemoIdentitySeeder
{
    // Public so the pilot scenario manifest can list the same intended role assignments.
    public sealed record DemoUser(string Username, string DisplayName, string Role, string? StationCode = null);

    public static readonly IReadOnlyList<DemoUser> Users =
    [
        new("demo-passenger", "Demo Passenger", Roles.Passenger),
        new("demo-driver", "Demo Driver", Roles.Driver),
        new("demo-controller", "Demo Controller", Roles.Controller),
        new("demo-station-gph", "Demo Station Staff (Gampaha)", Roles.StationStaff, "GPH"),
        new("demo-auditor", "Demo Auditor", Roles.Auditor),
        new("demo-sysadmin", "Demo SysAdmin", Roles.SysAdmin),
        new("demo-simoperator", "Demo Simulation Operator", Roles.SimOperator),
    ];

    // The demo driver is assigned to every seeded run of this train.
    public const string DriverTrain = "SIM-1001";

    public static async Task SeedAsync(
        SmartRailDbContext db, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var section = configuration.GetSection("DemoIdentities");
        var keycloakUrl = (section["KeycloakUrl"] ?? "https://localhost:8080").TrimEnd('/');
        var realm = section["Realm"] ?? "smartrail";
        var adminUser = section["AdminUser"] ?? "admin";
        var adminPassword = Required(section, "AdminPassword");
        var userPassword = Required(section, "UserPassword");
        var expectedIssuer = Required(configuration.GetSection("Identity"), "Issuer");

        using var http = new HttpClient();

        // The API and every client must see the same issuer, so check before creating anything.
        var discovery = await http.GetFromJsonAsync<JsonElement>(
            $"{keycloakUrl}/realms/{realm}/.well-known/openid-configuration", cancellationToken);
        var issuer = discovery.GetProperty("issuer").GetString();
        if (issuer != expectedIssuer)
        {
            throw new InvalidOperationException(
                $"Keycloak reports issuer '{issuer}' but Identity:Issuer is '{expectedIssuer}'. They must match exactly.");
        }

        http.DefaultRequestHeaders.Authorization = new("Bearer", await AdminTokenAsync(http, keycloakUrl, adminUser, adminPassword, cancellationToken));

        var stations = await db.Stations.AsNoTracking().ToDictionaryAsync(s => s.Code, s => s.Id, cancellationToken);
        var driverJourneys = await db.Journeys.AsNoTracking()
            .Where(j => j.DemoSeedKey != null && j.DemoSeedKey.StartsWith(DriverTrain + ":"))
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

        foreach (var user in Users)
        {
            var subject = await EnsureKeycloakUserAsync(http, keycloakUrl, realm, user, userPassword, cancellationToken);
            var accountId = await EnsureLocalAccountAsync(db, issuer!, subject, user, stations, cancellationToken);

            if (user.Role == Roles.Driver)
            {
                var assigned = await db.JourneyAssignments.Where(a => a.DriverId == accountId)
                    .Select(a => a.JourneyId).ToListAsync(cancellationToken);
                foreach (var journeyId in driverJourneys.Except(assigned))
                {
                    db.JourneyAssignments.Add(new JourneyAssignment
                    {
                        Id = Guid.NewGuid(), DriverId = accountId, JourneyId = journeyId,
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            Console.WriteLine($"Demo identity ready: {user.Username} ({user.Role})");
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Keycloak returned {(int)response.StatusCode}: {detail}");
        }
    }

    private static string Required(IConfigurationSection section, string key) =>
        section[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Set {section.Path}:{key} (user-secrets or environment).");

    private static async Task<string> AdminTokenAsync(
        HttpClient http, string keycloakUrl, string user, string password, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync($"{keycloakUrl}/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = user,
                ["password"] = password,
            }), cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private static async Task<string> EnsureKeycloakUserAsync(
        HttpClient http, string keycloakUrl, string realm, DemoUser user, string password, CancellationToken cancellationToken)
    {
        var users = $"{keycloakUrl}/admin/realms/{realm}/users";
        var found = await http.GetFromJsonAsync<JsonElement>(
            $"{users}?username={Uri.EscapeDataString(user.Username)}&exact=true", cancellationToken);

        if (found.GetArrayLength() == 0)
        {
            // A synthetic address on a reserved domain; the profile needs one to sign in.
            using var create = await http.PostAsJsonAsync(users, new
            {
                username = user.Username,
                enabled = true,
                emailVerified = true,
                firstName = "Demo",
                lastName = user.Username,
                email = $"{user.Username}@smartrail.invalid",
            }, cancellationToken);
            await EnsureSuccessAsync(create, cancellationToken);
            found = await http.GetFromJsonAsync<JsonElement>(
                $"{users}?username={Uri.EscapeDataString(user.Username)}&exact=true", cancellationToken);
        }

        var id = found[0].GetProperty("id").GetString()!;
        using var reset = await http.PutAsJsonAsync($"{users}/{id}/reset-password",
            new { type = "password", value = password, temporary = false }, cancellationToken);
        await EnsureSuccessAsync(reset, cancellationToken);
        return id;
    }

    private static async Task<Guid> EnsureLocalAccountAsync(
        SmartRailDbContext db, string issuer, string subject, DemoUser user,
        IReadOnlyDictionary<string, Guid> stations, CancellationToken cancellationToken)
    {
        // The demo account keeps one ID. If the Keycloak realm was recreated its user has a new
        // subject, so relink the account instead of adding a second one.
        var id = StableIds.For("demo-account", user.Username);
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            account = new Account
            {
                Id = id, Issuer = issuer, Subject = subject, DisplayName = user.DisplayName, Status = AccountStatus.Active,
            };
            db.Accounts.Add(account);
        }
        else
        {
            account.Issuer = issuer;
            account.Subject = subject;
        }

        await db.SaveChangesAsync(cancellationToken);

        Guid? stationId = user.StationCode is null ? null : stations[user.StationCode];
        var role = await db.Roles.SingleAsync(r => r.Code == user.Role, cancellationToken);
        var hasGrant = await db.RoleGrants.AnyAsync(
            g => g.AccountId == account.Id && g.RoleId == role.Id && g.StationScopeId == stationId, cancellationToken);
        if (!hasGrant)
        {
            db.RoleGrants.Add(new RoleGrant
            {
                Id = Guid.NewGuid(), AccountId = account.Id, RoleId = role.Id, StationScopeId = stationId,
            });
        }

        return account.Id;
    }
}
