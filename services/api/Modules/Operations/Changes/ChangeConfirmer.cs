using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Catering;
using Microsoft.EntityFrameworkCore;

namespace SmartRail.Api.Modules.Operations.Changes;

public abstract record ConfirmResult
{
    public sealed record Confirmed(Guid ChangeId, int JourneyVersion, ConfirmedStateDto State) : ConfirmResult;

    // Someone else changed the journey after the preview. Nothing was written.
    public sealed record Stale(int LatestVersion) : ConfirmResult;
}

// Makes a previewed change real. The change, its audit row and any Catering message are saved in
// one PostgreSQL transaction, so either all of them exist or none do.
public sealed class ChangeConfirmer(
    SmartRailDbContext db, IAuditTrail audit, ICateringOutbox outbox, ChangeStore store, TimeProvider time)
{
    public async Task<ConfirmResult> ConfirmAsync(
        HttpContext context, Guid actorId, OperationsJourney journey, JourneyChanges loaded, ChangePreview preview)
    {
        var cancellationToken = context.RequestAborted;
        var expected = preview.PreviewedVersion;
        var now = time.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The only thing that decides who wins. Two confirmations at the same version race for
        // this one row; PostgreSQL makes the second wait, then finds the version has moved on.
        var advanced = await db.JourneyVersions
            .Where(v => v.JourneyId == journey.Id && v.Version == expected)
            .ExecuteUpdateAsync(set => set
                .SetProperty(v => v.Version, expected + 1)
                .SetProperty(v => v.UpdatedAt, now), cancellationToken);
        if (advanced == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            var latest = await store.LoadAsync(journey.Id, cancellationToken);
            return new ConfirmResult.Stale(latest?.Version ?? expected);
        }

        var kind = preview.Change.Kind;
        var change = preview.Action == ChangeAction.Publish
            ? new ServiceChange(Guid.NewGuid(), expected + 1, ChangeAction.Publish, preview.Change)
            {
                JourneyId = journey.Id, PublishedById = actorId, PublishedAt = now,
            }
            : new ServiceChange(
                Guid.NewGuid(), expected + 1, ChangeAction.Withdraw, new ChangeDetails(kind), preview.WithdrawsChangeId)
            {
                JourneyId = journey.Id, PublishedById = actorId, PublishedAt = now,
            };
        db.ServiceChanges.Add(change);

        audit.Stage(
            context, actorId, $"change.{preview.Action.ToCode()}.{kind.ToCode()}".ToLowerInvariant(),
            AuditOutcomes.Succeeded, StaffChangeEndpoints.JourneyTarget, journey.Id);

        // A platform change is not food-related, so RailBites needs no message for it.
        if (kind != ChangeKind.Platform)
        {
            outbox.Enqueue(new TrainChangeMessage(
                change.Id,
                journey.Id,
                change.JourneyVersion,
                kind.ToCode(),
                preview.Action.ToCode(),
                preview.After.ConfirmedDelayMinutes,
                preview.After.OrderClosed,
                preview.After.BlockedPickupStopIds));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var state = ConfirmedState.Fold([.. loaded.History, change]);
        return new ConfirmResult.Confirmed(change.Id, change.JourneyVersion, ConfirmedStateDto.From(journey, change.JourneyVersion, state));
    }
}
