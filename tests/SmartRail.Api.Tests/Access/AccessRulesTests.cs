using SmartRail.Api.Modules.Access;

namespace SmartRail.Api.Tests.Access;

/// <summary>The pilot role matrix and scope rules, with no database or Keycloak.</summary>
public class AccessRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 6, 0, 0, TimeSpan.Zero);
    private static readonly Guid StationA = Guid.NewGuid();
    private static readonly Guid StationB = Guid.NewGuid();
    private static readonly Guid JourneyOne = Guid.NewGuid();
    private static readonly Guid JourneyTwo = Guid.NewGuid();

    private static AccessGrant Grant(string role, Guid? station = null, DateTimeOffset? validUntil = null) =>
        new(Guid.NewGuid(), role, station, validUntil, RoleMatrix.PermissionsOf(role).ToHashSet());

    private static AccessAccount Account(
        AccountStatus status = AccountStatus.Active, Guid[]? assigned = null, params AccessGrant[] grants) =>
        new(Guid.NewGuid(), status, grants, (assigned ?? []).ToHashSet());

    private static AccessDecision Check(AccessAccount account, string permission, AccessTarget target) =>
        AccessRules.Evaluate(account, permission, target, Now);

    /// <summary>The target a correctly scoped request for this permission would carry.</summary>
    private static AccessTarget TargetFor(AccessAccount account, string permission) => Permissions.Scopes[permission] switch
    {
        ScopeKind.Network => new AccessTarget.Network(),
        ScopeKind.Station => new AccessTarget.Station(StationA),
        ScopeKind.Journey => new AccessTarget.Journey(JourneyOne),
        _ => new AccessTarget.Own(account.Id),
    };

    private static AccessGrant GrantForMatrix(string role) =>
        Grant(role, role == Roles.StationStaff ? StationA : null);

    public static TheoryData<string, string[]> RoleMatrixRows => new()
    {
        { Roles.Passenger, [Permissions.SavedRoutesManage, Permissions.FollowsManage, Permissions.AlertsRead, Permissions.OrdersManage] },
        { Roles.Driver, [Permissions.DriverBriefingRead, Permissions.DriverReportsCreate] },
        { Roles.Controller, [Permissions.ChangesPublish, Permissions.IncidentsManage, Permissions.IncidentsRead] },
        { Roles.StationStaff, [Permissions.PlatformChangesPublish] },
        { Roles.Auditor, [Permissions.IncidentsRead, Permissions.AuditRead] },
        { Roles.SysAdmin, [Permissions.AccountsManage, Permissions.GrantsManage] },
        { Roles.SimOperator, [Permissions.SimulationControl] },
    };

    [Theory]
    [MemberData(nameof(RoleMatrixRows))]
    public void Each_role_is_allowed_exactly_its_matrix_permissions(string role, string[] expected)
    {
        var account = Account(assigned: [JourneyOne], grants: GrantForMatrix(role));

        var allowed = Permissions.Scopes.Keys
            .Where(p => Check(account, p, TargetFor(account, p)) == AccessDecision.Allowed)
            .Order()
            .ToList();

        Assert.Equal(expected.Order().ToList(), allowed);
    }

    [Fact]
    public void Every_role_has_a_matrix_row_and_every_matrix_permission_has_a_scope()
    {
        Assert.Equal(Roles.All.Order(), RoleMatrixRows.Select(row => (string)row[0]).Order());
        Assert.All(Roles.All, role =>
            Assert.All(RoleMatrix.PermissionsOf(role), p => Assert.True(Permissions.Scopes.ContainsKey(p), p)));
    }

    [Fact]
    public void Station_staff_may_act_only_at_the_granted_station()
    {
        var account = Account(grants: Grant(Roles.StationStaff, StationA));

        Assert.Equal(AccessDecision.Allowed,
            Check(account, Permissions.PlatformChangesPublish, new AccessTarget.Station(StationA)));
        Assert.Equal(AccessDecision.OutOfScope,
            Check(account, Permissions.PlatformChangesPublish, new AccessTarget.Station(StationB)));
    }

    [Fact]
    public void Station_staff_with_no_station_on_the_grant_have_no_station_access()
    {
        var account = Account(grants: Grant(Roles.StationStaff));

        Assert.Equal(AccessDecision.OutOfScope,
            Check(account, Permissions.PlatformChangesPublish, new AccessTarget.Station(StationA)));
    }

    [Fact]
    public void Station_staff_cannot_use_a_network_wide_permission_even_with_a_station()
    {
        var account = Account(grants: Grant(Roles.StationStaff, StationA));

        Assert.Equal(AccessDecision.NoPermission,
            Check(account, Permissions.ChangesPublish, new AccessTarget.Network()));
    }

    [Fact]
    public void A_controller_grant_limited_to_a_station_is_not_network_wide()
    {
        var account = Account(grants: Grant(Roles.Controller, StationA));

        Assert.Equal(AccessDecision.OutOfScope,
            Check(account, Permissions.ChangesPublish, new AccessTarget.Network()));
    }

    [Fact]
    public void A_driver_may_act_only_on_an_assigned_journey()
    {
        var account = Account(assigned: [JourneyOne], grants: Grant(Roles.Driver));

        Assert.Equal(AccessDecision.Allowed,
            Check(account, Permissions.DriverReportsCreate, new AccessTarget.Journey(JourneyOne)));
        Assert.Equal(AccessDecision.OutOfScope,
            Check(account, Permissions.DriverReportsCreate, new AccessTarget.Journey(JourneyTwo)));
    }

    [Fact]
    public void An_assignment_without_a_driver_grant_authorizes_nothing()
    {
        var account = Account(assigned: [JourneyOne], grants: Grant(Roles.Passenger));

        Assert.Equal(AccessDecision.NoPermission,
            Check(account, Permissions.DriverBriefingRead, new AccessTarget.Journey(JourneyOne)));
    }

    [Fact]
    public void A_passenger_is_limited_to_their_own_data()
    {
        var account = Account(grants: Grant(Roles.Passenger));

        Assert.Equal(AccessDecision.Allowed,
            Check(account, Permissions.OrdersManage, new AccessTarget.Own(account.Id)));
        Assert.Equal(AccessDecision.OutOfScope,
            Check(account, Permissions.OrdersManage, new AccessTarget.Own(Guid.NewGuid())));
    }

    [Theory]
    [InlineData(Roles.Controller, Permissions.ChangesPublish)]
    [InlineData(Roles.SysAdmin, Permissions.AccountsManage)]
    [InlineData(Roles.Auditor, Permissions.AuditRead)]
    public void A_disabled_account_is_denied_even_with_an_active_grant(string role, string permission)
    {
        var account = Account(AccountStatus.Disabled, grants: Grant(role));

        Assert.Equal(AccessDecision.AccountDisabled, Check(account, permission, new AccessTarget.Network()));
    }

    [Fact]
    public void A_grant_is_active_until_valid_until_and_denied_from_then_on()
    {
        var before = Account(grants: Grant(Roles.Controller, validUntil: Now.AddMinutes(1)));
        var at = Account(grants: Grant(Roles.Controller, validUntil: Now));
        var after = Account(grants: Grant(Roles.Controller, validUntil: Now.AddMinutes(-1)));
        var noLimit = Account(grants: Grant(Roles.Controller));

        Assert.Equal(AccessDecision.Allowed, Check(before, Permissions.ChangesPublish, new AccessTarget.Network()));
        Assert.Equal(AccessDecision.NoPermission, Check(at, Permissions.ChangesPublish, new AccessTarget.Network()));
        Assert.Equal(AccessDecision.NoPermission, Check(after, Permissions.ChangesPublish, new AccessTarget.Network()));
        Assert.Equal(AccessDecision.Allowed, Check(noLimit, Permissions.ChangesPublish, new AccessTarget.Network()));
    }

    [Fact]
    public void An_expired_station_grant_does_not_stop_a_live_grant_at_another_station()
    {
        var account = Account(grants:
        [
            Grant(Roles.StationStaff, StationA, validUntil: Now.AddDays(-1)),
            Grant(Roles.StationStaff, StationB),
        ]);

        Assert.Equal(AccessDecision.OutOfScope,
            Check(account, Permissions.PlatformChangesPublish, new AccessTarget.Station(StationA)));
        Assert.Equal(AccessDecision.Allowed,
            Check(account, Permissions.PlatformChangesPublish, new AccessTarget.Station(StationB)));
    }

    [Fact]
    public void Permission_and_scope_must_come_from_the_same_grant()
    {
        // Platform permission only at A, and a different live grant that happens to have station B.
        var account = Account(grants:
        [
            Grant(Roles.StationStaff, StationA),
            Grant(Roles.Auditor, StationB),
        ]);

        Assert.Equal(AccessDecision.OutOfScope,
            Check(account, Permissions.PlatformChangesPublish, new AccessTarget.Station(StationB)));
    }

    [Theory]
    [InlineData(Roles.SysAdmin)]
    [InlineData(Roles.Auditor)]
    public void SysAdmin_and_Auditor_cannot_make_operational_changes(string role)
    {
        var account = Account(assigned: [JourneyOne], grants: Grant(role));

        Assert.Equal(AccessDecision.NoPermission,
            Check(account, Permissions.ChangesPublish, new AccessTarget.Network()));
        Assert.Equal(AccessDecision.NoPermission,
            Check(account, Permissions.PlatformChangesPublish, new AccessTarget.Station(StationA)));
        Assert.Equal(AccessDecision.NoPermission,
            Check(account, Permissions.IncidentsManage, new AccessTarget.Network()));
        Assert.Equal(AccessDecision.NoPermission,
            Check(account, Permissions.DriverReportsCreate, new AccessTarget.Journey(JourneyOne)));
    }

    [Fact]
    public void Auditor_reads_but_cannot_manage_accounts()
    {
        var account = Account(grants: Grant(Roles.Auditor));

        Assert.Equal(AccessDecision.Allowed, Check(account, Permissions.AuditRead, new AccessTarget.Network()));
        Assert.Equal(AccessDecision.NoPermission, Check(account, Permissions.AccountsManage, new AccessTarget.Network()));
    }

    [Fact]
    public void No_grants_unknown_permissions_and_a_mismatched_target_are_denied()
    {
        var none = Account();
        var controller = Account(grants: Grant(Roles.Controller));

        Assert.Equal(AccessDecision.NoPermission, Check(none, Permissions.ChangesPublish, new AccessTarget.Network()));
        Assert.Equal(AccessDecision.NoPermission, Check(controller, "trains.delete", new AccessTarget.Network()));
        Assert.Equal(AccessDecision.OutOfScope,
            Check(controller, Permissions.ChangesPublish, new AccessTarget.Station(StationA)));
    }
}
