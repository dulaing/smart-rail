namespace SmartRail.Api.Modules.Access;

public enum AccountStatus
{
    Active,
    Disabled,
}

public static class AccountStatuses
{
    public static string ToCode(this AccountStatus status) => status switch
    {
        AccountStatus.Active => "ACTIVE",
        AccountStatus.Disabled => "DISABLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static AccountStatus FromCode(string code) => code switch
    {
        "ACTIVE" => AccountStatus.Active,
        "DISABLED" => AccountStatus.Disabled,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown account status."),
    };
}

/// <summary>One role given to an account, with the permissions that role currently carries.</summary>
public sealed record AccessGrant(
    Guid Id,
    string RoleCode,
    Guid? StationScopeId,
    DateTimeOffset? ValidUntil,
    IReadOnlySet<string> Permissions)
{
    public bool IsActiveAt(DateTimeOffset now) => ValidUntil is null || ValidUntil > now;
}

/// <summary>Everything the rules need to know about one local account, read fresh from the database.</summary>
public sealed record AccessAccount(
    Guid Id,
    AccountStatus Status,
    IReadOnlyList<AccessGrant> Grants,
    IReadOnlySet<Guid> AssignedJourneyIds);

/// <summary>
/// What the caller wants to act on. Build it from server data, never from a client-supplied
/// station or journey claim.
/// </summary>
public abstract record AccessTarget
{
    public sealed record Network : AccessTarget;

    public sealed record Station(Guid StationId) : AccessTarget;

    public sealed record Journey(Guid JourneyId) : AccessTarget;

    /// <summary>Personal data owned by <paramref name="OwnerAccountId"/>.</summary>
    public sealed record Own(Guid OwnerAccountId) : AccessTarget;
}

public enum AccessDecision
{
    Allowed,
    AccountDisabled,
    NoPermission,
    OutOfScope,
}

/// <summary>
/// The pilot's authorization rules as pure functions, so they can be tested without a
/// database. The default answer is denial: access needs an enabled account and one active
/// grant whose role carries the permission and whose scope covers the target.
/// </summary>
public static class AccessRules
{
    public static AccessDecision Evaluate(
        AccessAccount account, string permission, AccessTarget target, DateTimeOffset now)
    {
        if (account.Status != AccountStatus.Active)
        {
            return AccessDecision.AccountDisabled;
        }

        if (!Permissions.Scopes.TryGetValue(permission, out var scope))
        {
            return AccessDecision.NoPermission;
        }

        // Permission and scope are judged on the same grant, so a permission held at one
        // station cannot be combined with a station granted by something else.
        var candidates = account.Grants
            .Where(g => g.IsActiveAt(now) && g.Permissions.Contains(permission))
            .ToList();
        if (candidates.Count == 0)
        {
            return AccessDecision.NoPermission;
        }

        return candidates.Any(g => CoversTarget(account, g, scope, target))
            ? AccessDecision.Allowed
            : AccessDecision.OutOfScope;
    }

    private static bool CoversTarget(AccessAccount account, AccessGrant grant, ScopeKind scope, AccessTarget target) =>
        (scope, target) switch
        {
            // A station-limited grant never confers network-wide authority.
            (ScopeKind.Network, AccessTarget.Network) => grant.StationScopeId is null,
            (ScopeKind.Station, AccessTarget.Station station) => grant.StationScopeId == station.StationId,
            (ScopeKind.Journey, AccessTarget.Journey journey) =>
                grant.StationScopeId is null && account.AssignedJourneyIds.Contains(journey.JourneyId),
            (ScopeKind.Own, AccessTarget.Own own) => grant.StationScopeId is null && own.OwnerAccountId == account.Id,
            _ => false,
        };
}

/// <summary>What an account may do right now, for a client to show. The API still checks every request.</summary>
public sealed record EffectivePermission(
    string Code, ScopeKind Scope, IReadOnlyList<Guid> StationIds, IReadOnlyList<Guid> JourneyIds);

public static class EffectivePermissions
{
    /// <summary>
    /// The permissions an enabled account holds through active grants. A permission appears only
    /// when something it can act on exists: a station for station scope, an assignment for
    /// journey scope. A disabled account has none.
    /// </summary>
    public static IReadOnlyList<EffectivePermission> Of(AccessAccount account, DateTimeOffset now)
    {
        if (account.Status != AccountStatus.Active)
        {
            return [];
        }

        var active = account.Grants.Where(g => g.IsActiveAt(now)).ToList();
        var result = new List<EffectivePermission>();

        foreach (var (code, scope) in Permissions.Scopes.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            var holders = active.Where(g => g.Permissions.Contains(code)).ToList();
            var unrestricted = holders.Any(g => g.StationScopeId is null);
            var stationIds = holders.Where(g => g.StationScopeId is not null)
                .Select(g => g.StationScopeId!.Value).Distinct().Order().ToList();

            var effective = scope switch
            {
                ScopeKind.Station when stationIds.Count > 0 =>
                    new EffectivePermission(code, scope, stationIds, []),
                ScopeKind.Journey when unrestricted && account.AssignedJourneyIds.Count > 0 =>
                    new EffectivePermission(code, scope, [], account.AssignedJourneyIds.Order().ToList()),
                ScopeKind.Network or ScopeKind.Own when unrestricted =>
                    new EffectivePermission(code, scope, [], []),
                _ => null,
            };

            if (effective is not null)
            {
                result.Add(effective);
            }
        }

        return result;
    }
}
