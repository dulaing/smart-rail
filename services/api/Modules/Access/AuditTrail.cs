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

// Some targets of one type, for a trail that follows an entity and the things it led to.
public sealed record AuditTargets(string TargetType, IReadOnlyCollection<Guid> TargetIds);

// One oldest-first page of a trail and whether more entries follow it. Entries holds at most the
// requested size; HasMore says whether the trail continues, so a caller never mistakes a full page
// for the end of the history.
public sealed record AuditPage(IReadOnlyList<AuditRecord> Entries, bool HasMore);

// The small door other modules use for audit work, so they never touch Access's tables.
public interface IAuditTrail
{
    // Adds a row to the current unit of work. It is stored when the caller saves, so it commits
    // or rolls back together with the caller's own change.
    void Stage(HttpContext context, Guid actorId, string action, string outcome, string targetType, Guid? targetId);

    // The oldest-first entries about one target, at most limit of them.
    Task<IReadOnlyList<AuditRecord>> ReadForTargetAsync(
        string targetType, Guid targetId, int limit, CancellationToken cancellationToken);

    // The oldest-first entries about any of the targets, at most limit of them.
    Task<IReadOnlyList<AuditRecord>> ReadForTargetsAsync(
        IReadOnlyList<AuditTargets> targets, int limit, CancellationToken cancellationToken);

    // One oldest-first page about any of the targets, at most limit entries, starting strictly
    // after the entry at (afterTime, afterId). A null position starts at the oldest entry. The
    // page carries entries plus whether another page follows.
    Task<AuditPage> ReadPageAsync(
        IReadOnlyList<AuditTargets> targets, DateTimeOffset? afterTime, Guid? afterId, int limit,
        CancellationToken cancellationToken);
}

public sealed class AuditTrail(SmartRailDbContext db, AccessGuard guard) : IAuditTrail
{
    public void Stage(HttpContext context, Guid actorId, string action, string outcome, string targetType, Guid? targetId) =>
        db.AuditEntries.Add(guard.NewEntry(context, actorId, action, outcome, targetType, targetId));

    public Task<IReadOnlyList<AuditRecord>> ReadForTargetAsync(
        string targetType, Guid targetId, int limit, CancellationToken cancellationToken) =>
        ReadForTargetsAsync([new AuditTargets(targetType, [targetId])], limit, cancellationToken);

    public async Task<IReadOnlyList<AuditRecord>> ReadForTargetsAsync(
        IReadOnlyList<AuditTargets> targets, int limit, CancellationToken cancellationToken) =>
        (await ReadPageAsync(targets, null, null, limit, cancellationToken)).Entries;

    public async Task<AuditPage> ReadPageAsync(
        IReadOnlyList<AuditTargets> targets, DateTimeOffset? afterTime, Guid? afterId, int limit,
        CancellationToken cancellationToken)
    {
        // The next page's entries are among the next limit+1 entries of each type, so a per-type
        // read is enough. One more than the page shows is taken, to tell a full page from the end.
        var records = new List<AuditRecord>();
        foreach (var (targetType, targetIds) in targets.Where(t => t.TargetIds.Count > 0))
        {
            var query = db.AuditEntries.AsNoTracking()
                .Where(e => e.TargetType == targetType && e.TargetId != null && targetIds.Contains(e.TargetId.Value));

            if (afterTime is { } time && afterId is { } id)
            {
                query = query.Where(e => e.OccurredAt > time || (e.OccurredAt == time && e.Id.CompareTo(id) > 0));
            }

            var rows = await query
                .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
                .Take(limit + 1)
                .Select(e => new
                {
                    Entry = e,
                    ActorName = db.Accounts.Where(a => a.Id == e.ActorId).Select(a => a.DisplayName).First(),
                })
                .ToListAsync(cancellationToken);

            records.AddRange(rows.Select(r => new AuditRecord(
                r.Entry.Id, r.Entry.ActorId, r.ActorName, r.Entry.Action, r.Entry.Outcome,
                r.Entry.TargetType, r.Entry.TargetId, r.Entry.OccurredAt, r.Entry.CorrelationId)));
        }

        var ordered = records.OrderBy(r => r.OccurredAt).ThenBy(r => r.Id).ToList();
        return new AuditPage(ordered.Take(limit).ToList(), ordered.Count > limit);
    }
}
