using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Access;

// The small door other modules use to ask which journeys the signed-in driver may use, so they
// never query the assignment table.
public interface IDriverAssignments
{
    // The journeys the caller is assigned to and holds the journey-scoped permission for, read
    // fresh on every call. Empty for a driver with no assignment. Null when the account is
    // disabled or holds the permission through no grant at all; the refusal is audited.
    Task<IReadOnlyList<Guid>?> JourneysAsync(HttpContext context, string permission);
}

public sealed class DriverAssignments(
    CurrentAccount current, AccessGuard guard, SmartRailDbContext db, TimeProvider time) : IDriverAssignments
{
    public async Task<IReadOnlyList<Guid>?> JourneysAsync(HttpContext context, string permission)
    {
        var account = await current.GetAsync(context.User, context.RequestAborted);
        if (account is null)
        {
            return null;
        }

        var now = time.GetUtcNow();

        // Asking about no journey in particular tells "no such permission" (refused) apart from
        // "not assigned to this one" (an empty list).
        var holds = AccessRules.Evaluate(account.Access, permission, new AccessTarget.Journey(Guid.Empty), now);
        if (holds is AccessDecision.AccountDisabled or AccessDecision.NoPermission)
        {
            db.AuditEntries.Add(guard.NewEntry(context, account.Id, permission, AuditOutcomes.Denied, "ACCOUNT", account.Id));
            await db.SaveChangesAsync(context.RequestAborted);
            return null;
        }

        return account.Access.AssignedJourneyIds
            .Where(id => AccessRules.Evaluate(account.Access, permission, new AccessTarget.Journey(id), now) == AccessDecision.Allowed)
            .Order()
            .ToList();
    }
}
