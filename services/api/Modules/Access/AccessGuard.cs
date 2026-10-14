using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Access;

/// <summary>
/// Checks a permission and target for the signed-in account against the database, on every
/// call, and records a denied action. A feature builds the target from server data (the
/// station of the stop being changed, the journey of the report) and calls this before it
/// acts; it never trusts a station or journey the client claims.
/// </summary>
public sealed class AccessGuard(CurrentAccount current, SmartRailDbContext db, TimeProvider time)
{
    public async Task<AccessDecision> CheckAsync(HttpContext context, string permission, AccessTarget target)
    {
        var account = await current.GetAsync(context.User, context.RequestAborted);
        if (account is null)
        {
            // No local account means no actor to audit; the caller is told no.
            return AccessDecision.NoPermission;
        }

        var decision = AccessRules.Evaluate(account.Access, permission, target, time.GetUtcNow());
        if (decision != AccessDecision.Allowed)
        {
            var (targetType, targetId) = Describe(target);
            db.AuditEntries.Add(NewEntry(context, account.Id, permission, AuditOutcomes.Denied, targetType, targetId));
            await db.SaveChangesAsync(context.RequestAborted);
        }

        return decision;
    }

    /// <summary>An audit row for the caller to save in the same transaction as its change.</summary>
    public AuditEntry NewEntry(
        HttpContext context, Guid actorId, string action, string outcome, string targetType, Guid? targetId) =>
        new()
        {
            Id = Guid.NewGuid(),
            ActorId = actorId,
            Action = action,
            Outcome = outcome,
            TargetType = targetType,
            TargetId = targetId,
            CorrelationId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
            OccurredAt = time.GetUtcNow(),
        };

    private static (string Type, Guid? Id) Describe(AccessTarget target) => target switch
    {
        AccessTarget.Station station => ("STATION", station.StationId),
        AccessTarget.Journey journey => ("JOURNEY", journey.JourneyId),
        AccessTarget.Own own => ("ACCOUNT", own.OwnerAccountId),
        _ => ("NETWORK", null),
    };
}

/// <summary>A network-wide permission. Station and journey permissions go through <see cref="AccessGuard"/>.</summary>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionHandler(AccessGuard guard) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.Resource is HttpContext http
            && await guard.CheckAsync(http, requirement.Permission, new AccessTarget.Network()) == AccessDecision.Allowed)
        {
            context.Succeed(requirement);
        }
    }
}
