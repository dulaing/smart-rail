using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Access;

// An audit entry with the actor's display name, for a person allowed to read the trail.
public sealed record AuditRecord(
    Guid Id,
    Guid ActorId,
    string ActorName,
    string Action,
    string Outcome,
    string TargetType,
    Guid? TargetId,
    DateTimeOffset OccurredAt,
    string CorrelationId);

// The small door other modules use for audit work, so they never touch Access's tables.
public interface IAuditTrail
{
    // Adds a row to the current unit of work. It is stored when the caller saves, so it commits
    // or rolls back together with the caller's own change.
    void Stage(HttpContext context, Guid actorId, string action, string outcome, string targetType, Guid? targetId);

    // The oldest-first entries about one target, at most limit of them.
    Task<IReadOnlyList<AuditRecord>> ReadForTargetAsync(
        string targetType, Guid targetId, int limit, CancellationToken cancellationToken);
}

public sealed class AuditTrail(SmartRailDbContext db, AccessGuard guard) : IAuditTrail
{
    public void Stage(HttpContext context, Guid actorId, string action, string outcome, string targetType, Guid? targetId) =>
        db.AuditEntries.Add(guard.NewEntry(context, actorId, action, outcome, targetType, targetId));

    public async Task<IReadOnlyList<AuditRecord>> ReadForTargetAsync(
        string targetType, Guid targetId, int limit, CancellationToken cancellationToken)
    {
        var rows = await db.AuditEntries.AsNoTracking()
            .Where(e => e.TargetType == targetType && e.TargetId == targetId)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .Take(limit)
            .Select(e => new
            {
                Entry = e,
                ActorName = db.Accounts.Where(a => a.Id == e.ActorId).Select(a => a.DisplayName).First(),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new AuditRecord(
            r.Entry.Id, r.Entry.ActorId, r.ActorName, r.Entry.Action, r.Entry.Outcome,
            r.Entry.TargetType, r.Entry.TargetId, r.Entry.OccurredAt, r.Entry.CorrelationId)).ToList();
    }
}
