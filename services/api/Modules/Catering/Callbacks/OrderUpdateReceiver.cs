using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Catering.Orders;
using SmartRail.Api.Modules.Catering.Provider;

namespace SmartRail.Api.Modules.Catering.Callbacks;

public abstract record ReceiveResult
{
    public sealed record Applied(string Result) : ReceiveResult;

    public sealed record Duplicate : ReceiveResult;

    // The event ID was already received with different content.
    public sealed record Conflict : ReceiveResult;

    public sealed record UnknownLink : ReceiveResult;

    // The link has no order details yet and the provider could not supply them, so the callback is
    // not acknowledged: RailBites keeps it and sends it again.
    public sealed record NotReady : ReceiveResult;

    public sealed record Invalid(string Detail) : ReceiveResult;
}

// What RailBites sends when an order's status or pickup time changes.
public sealed record OrderUpdate(
    string EventId,
    Guid OrderLinkId,
    string ProviderOrderRef,
    string Status,
    int StatusSequence,
    string? ReasonCode,
    ProviderPickupTime PickupTime);

// Takes in one authenticated callback. The receipt and the conditional update to the order link
// commit together or not at all. Only a greater status sequence changes the link, so a repeat or an
// older callback is accepted (so RailBites can stop sending it) and does nothing.
public sealed class OrderUpdateReceiver(SmartRailDbContext db, IRailBitesClient provider, OrderLinkWrites writes, TimeProvider time)
{
    private static readonly string[] Statuses = ["ACCEPTED", "READY", "COLLECTED", "NO_SHOW", "CANCELLED"];

    public static string DigestOf(ReadOnlySpan<byte> body) => Convert.ToHexStringLower(SHA256.HashData(body));

    public async Task<ReceiveResult> ReceiveAsync(byte[] body, CancellationToken cancellationToken)
    {
        var update = Parse(body, out var problem);
        if (update is null)
        {
            return new ReceiveResult.Invalid(problem!);
        }

        var digest = DigestOf(body);
        var now = time.GetUtcNow();

        // A callback can arrive before the placement answer, or for an order SmartRail called
        // refused. Such a link has no pickup code or items yet, and a callback does not carry them,
        // so they are fetched from the provider first. If they cannot be, the callback is not
        // acknowledged. A cancellation for a refused order needs nothing: it stays refused.
        var state = await db.OrderLinks.AsNoTracking()
            .Where(l => l.Id == update.OrderLinkId).Select(l => l.PlacementState).SingleOrDefaultAsync(cancellationToken);
        if (state is null)
        {
            return new ReceiveResult.UnknownLink();
        }

        if (state == PlacementStates.Pending || (state == PlacementStates.Rejected && update.Status != "CANCELLED"))
        {
            var found = await provider.LookupAsync(update.OrderLinkId, cancellationToken);
            if (found.Outcome != ProviderOutcome.Success)
            {
                return new ReceiveResult.NotReady();
            }

            await writes.ApplyProviderOrderAsync(update.OrderLinkId, found.Value!, now, cancellationToken);
        }

        // The unique event ID decides a race between two identical deliveries: the loser's insert
        // fails, its transaction rolls back, and it is answered as the duplicate it is.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var seen = await db.WebhookReceipts.AsNoTracking()
                .SingleOrDefaultAsync(r => r.ProviderEventId == update.EventId, cancellationToken);
            if (seen is not null)
            {
                return seen.ContentDigest == digest ? new ReceiveResult.Duplicate() : new ReceiveResult.Conflict();
            }

            if (!await db.OrderLinks.AsNoTracking().AnyAsync(l => l.Id == update.OrderLinkId, cancellationToken))
            {
                return new ReceiveResult.UnknownLink();
            }

            // The update is decided first and the receipt records what it did, both in this one
            // transaction: if either fails, neither is kept and the redelivery starts clean.
            var changed = await ApplyAsync(update, now, cancellationToken);
            try
            {
                db.WebhookReceipts.Add(new WebhookReceipt
                {
                    ProviderEventId = update.EventId,
                    OrderLinkId = update.OrderLinkId,
                    StatusSequence = update.StatusSequence,
                    ContentDigest = digest,
                    ReceivedAt = now,
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();
                continue;
            }

            await transaction.CommitAsync(cancellationToken);
            return new ReceiveResult.Applied(changed ? "APPLIED" : "STALE");
        }

        return new ReceiveResult.Duplicate();
    }

    // One conditional UPDATE decides whether this callback is newer than what the link holds.
    private async Task<bool> ApplyAsync(OrderUpdate update, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var id = update.OrderLinkId;
        var sequence = update.StatusSequence;

        // A refused order's cancellation changes nothing: the passenger was never shown an order.
        return await db.OrderLinks
            .Where(l => l.Id == id && l.PlacementState != PlacementStates.Rejected && l.StatusSequence < sequence)
            .ExecuteUpdateAsync(set => set
                .SetProperty(l => l.ProviderOrderRef, update.ProviderOrderRef)
                .SetProperty(l => l.ProviderStatus, update.Status)
                .SetProperty(l => l.StatusSequence, sequence)
                .SetProperty(l => l.CancellationReason, update.ReasonCode)
                .SetProperty(l => l.ScheduledArrival, update.PickupTime.ScheduledArrival)
                .SetProperty(l => l.EstimatedArrival, update.PickupTime.EstimatedArrival)
                .SetProperty(l => l.EtaEvidenceAt, update.PickupTime.EtaEvidenceAt)
                .SetProperty(l => l.UpdatedAt, now), cancellationToken) > 0;
    }

    private static OrderUpdate? Parse(byte[] body, out string? problem)
    {
        problem = null;
        try
        {
            var update = JsonSerializer.Deserialize<OrderUpdate>(body, ProviderJson.Options);
            if (update is null || string.IsNullOrWhiteSpace(update.EventId) || update.EventId.Length > 100
                || update.OrderLinkId == Guid.Empty || string.IsNullOrWhiteSpace(update.ProviderOrderRef)
                || !Statuses.Contains(update.Status) || update.StatusSequence < 1 || update.PickupTime is null)
            {
                problem = "The callback is missing a required field or has an invalid one.";
                return null;
            }

            return update;
        }
        catch (JsonException)
        {
            problem = "The callback body is not valid JSON of the expected shape.";
            return null;
        }
    }
}
