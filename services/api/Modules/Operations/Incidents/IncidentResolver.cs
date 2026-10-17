using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Access;

namespace SmartRail.Api.Modules.Operations.Incidents;

public abstract record ResolveResult
{
    // This request resolved it.
    public sealed record Resolved(Incident Incident) : ResolveResult;

    // It was resolved already, by this controller or another. Its resolution time is unchanged.
    public sealed record AlreadyResolved(Incident Incident) : ResolveResult;

    public sealed record NotFound : ResolveResult;
}

// Resolves an incident once. The update only matches an incident that is still open, so a repeat
// or a concurrent request changes nothing and writes no second audit row. Resolving touches
// nothing else: not the report's acknowledgement, and not any change already published.
public sealed class IncidentResolver(SmartRailDbContext db, IAuditTrail audit, IncidentStore store, TimeProvider time)
{
    public const string ResolveAction = "incident.resolve";

    public async Task<ResolveResult> ResolveAsync(HttpContext context, Guid controllerId, Guid incidentId)
    {
        var cancellationToken = context.RequestAborted;
        var now = PostgresTime.Round(time.GetUtcNow());

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var won = await db.Incidents
            .Where(i => i.Id == incidentId && i.Status == IncidentStates.Open)
            .ExecuteUpdateAsync(set => set
                .SetProperty(i => i.Status, IncidentStates.Resolved)
                .SetProperty(i => i.ResolvedAt, now), cancellationToken);

        if (won == 1)
        {
            audit.Stage(context, controllerId, ResolveAction, AuditOutcomes.Succeeded, IncidentOpener.IncidentTarget, incidentId);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        var incident = await store.FindAsync(incidentId, cancellationToken);
        return incident is null
            ? new ResolveResult.NotFound()
            : won == 1
                ? new ResolveResult.Resolved(incident)
                : new ResolveResult.AlreadyResolved(incident);
    }
}
