namespace SmartRail.Api.Modules.Access;

// Pilot role codes. Guest is not a role: it is a caller with no account at all. The RailBites
// service is not an account either; its webhooks use a signed HMAC contract.
public static class Roles
{
    public const string Passenger = "PASSENGER";
    public const string Driver = "DRIVER";
    public const string Controller = "CONTROLLER";
    public const string StationStaff = "STATION_STAFF";
    public const string Auditor = "AUDITOR";
    public const string SysAdmin = "SYS_ADMIN";
    public const string SimOperator = "SIM_OPERATOR";

    public static readonly IReadOnlyList<string> All =
        [Passenger, Driver, Controller, StationStaff, Auditor, SysAdmin, SimOperator];
}

// What a permission is limited to. The scope belongs to the permission, not the role.
public enum ScopeKind
{
    // Pilot-wide. Only a grant that is not limited to one station confers it.
    Network,

    // One station. The grant's station must be the target station.
    Station,

    // One journey. The driver must be assigned to the target journey.
    Journey,

    // The caller's own data. The target must name the caller's own account.
    Own,
}

public static class Permissions
{
    public const string SavedRoutesManage = "passenger.saved_routes.manage";
    public const string FollowsManage = "passenger.follows.manage";
    public const string AlertsRead = "passenger.alerts.read";
    public const string OrdersManage = "orders.manage";

    public const string DriverBriefingRead = "driver.briefing.read";
    public const string DriverReportsCreate = "driver.reports.create";

    public const string ChangesPublish = "changes.publish";
    public const string PlatformChangesPublish = "changes.publish_platform";
    public const string IncidentsManage = "incidents.manage";
    public const string IncidentsRead = "incidents.read";
    public const string AuditRead = "audit.read";

    public const string AccountsManage = "accounts.manage";
    public const string GrantsManage = "grants.manage";

    public const string SimulationControl = "simulation.control";

    // Every permission and its scope. A code that is not listed is always denied.
    public static readonly IReadOnlyDictionary<string, ScopeKind> Scopes = new Dictionary<string, ScopeKind>
    {
        [SavedRoutesManage] = ScopeKind.Own,
        [FollowsManage] = ScopeKind.Own,
        [AlertsRead] = ScopeKind.Own,
        [OrdersManage] = ScopeKind.Own,
        [DriverBriefingRead] = ScopeKind.Journey,
        [DriverReportsCreate] = ScopeKind.Journey,
        [ChangesPublish] = ScopeKind.Network,
        [PlatformChangesPublish] = ScopeKind.Station,
        [IncidentsManage] = ScopeKind.Network,
        [IncidentsRead] = ScopeKind.Network,
        [AuditRead] = ScopeKind.Network,
        [AccountsManage] = ScopeKind.Network,
        [GrantsManage] = ScopeKind.Network,
        [SimulationControl] = ScopeKind.Network,
    };
}

// The seeded pilot role matrix from "Who can do what" in the scope decision. It seeds
// identity.role_permissions; at run time the database rows are what authorize, so a
// later role is a new row plus a test here. No role has a permission it was not given:
// SysAdmin manages accounts and grants only, and Auditor only reads.
public static class RoleMatrix
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ByRole =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [Roles.Passenger] = [Permissions.SavedRoutesManage, Permissions.FollowsManage, Permissions.AlertsRead, Permissions.OrdersManage],
            [Roles.Driver] = [Permissions.DriverBriefingRead, Permissions.DriverReportsCreate],
            [Roles.Controller] = [Permissions.ChangesPublish, Permissions.IncidentsManage, Permissions.IncidentsRead],
            [Roles.StationStaff] = [Permissions.PlatformChangesPublish],
            [Roles.Auditor] = [Permissions.IncidentsRead, Permissions.AuditRead],
            [Roles.SysAdmin] = [Permissions.AccountsManage, Permissions.GrantsManage],
            [Roles.SimOperator] = [Permissions.SimulationControl],
        };

    public static IReadOnlyList<string> PermissionsOf(string role) =>
        ByRole.TryGetValue(role, out var permissions) ? permissions : [];
}
