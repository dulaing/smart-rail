using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Catering.Provider;

namespace SmartRail.Api.Modules.Catering.Orders;

public abstract record CreateResult
{
    // A new link was stored and tried once. Link may be confirmed, rejected or still pending.
    public sealed record Created(OrderLink Link) : CreateResult;

    // The same account sent the same request again; this is the link stored the first time.
    public sealed record Replayed(OrderLink Link) : CreateResult;

    // The key already belongs to a different request.
    public sealed record KeyInUse : CreateResult;

    // SmartRail can already see this pickup is not available, so nothing was stored or sent.
    public sealed record Unavailable(string Problem) : CreateResult;
}

// Starts one passenger order. The request is saved as a pending link before RailBites is called,
// so a crash or timeout leaves something to retry. (account, key) is unique in the database, so
// concurrent identical submissions end up with the same link.
public sealed class OrderCreator(
    SmartRailDbContext db, PickupAssessor assessor, PlacementProcessor processor, TimeProvider time)
{
    public static string DigestOf(string collectionPointId, Guid journeyId, Guid stopId, IEnumerable<RequestedItem> items)
    {
        var lines = items.OrderBy(i => i.MenuItemId, StringComparer.Ordinal).Select(i => $"{i.MenuItemId}:{i.Quantity}");
        var text = $"{collectionPointId}|{journeyId}|{stopId}|{string.Join(';', lines)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public async Task<CreateResult> CreateAsync(
        Guid accountId, string clientRequestKey, string collectionPointId, Guid journeyId, Guid stopId,
        IReadOnlyList<RequestedItem> items, CancellationToken cancellationToken)
    {
        var digest = DigestOf(collectionPointId, journeyId, stopId, items);

        if (await FindAsync(accountId, clientRequestKey, cancellationToken) is { } existing)
        {
            return await ReplayAsync(existing, digest, cancellationToken);
        }

        var assessment = await assessor.AssessAsync(journeyId, stopId, cancellationToken);
        if (assessment.Problem is not null)
        {
            return new CreateResult.Unavailable(assessment.Problem);
        }

        var now = time.GetUtcNow();
        var link = new OrderLink
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            JourneyId = journeyId,
            PickupStopId = stopId,
            ClientRequestKey = clientRequestKey,
            CollectionPointId = collectionPointId,
            RequestDigest = digest,
            RequestPayload = JsonSerializer.Serialize(new StoredRequest(collectionPointId, journeyId, stopId, items), ProviderJson.Options),
            PlacementState = PlacementStates.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.OrderLinks.Add(link);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent identical submission won the race. Return what it stored.
            db.ChangeTracker.Clear();
            var winner = await FindAsync(accountId, clientRequestKey, cancellationToken)
                ?? throw new InvalidOperationException("Order link vanished after a unique violation.");
            return await ReplayAsync(winner, digest, cancellationToken);
        }

        return new CreateResult.Created(await processor.AttemptAsync(link.Id, cancellationToken));
    }

    private async Task<CreateResult> ReplayAsync(OrderLink existing, string digest, CancellationToken cancellationToken)
    {
        if (existing.RequestDigest != digest)
        {
            return new CreateResult.KeyInUse();
        }

        // A retry of a request whose outcome is unknown is a good moment to ask again. The processor
        // recomputes eligibility first, so it never places a new order that is no longer allowed.
        if (existing.PlacementState == PlacementStates.Pending && existing.NextAttemptAt <= time.GetUtcNow())
        {
            existing = await processor.AttemptAsync(existing.Id, cancellationToken);
        }

        return new CreateResult.Replayed(existing);
    }

    private Task<OrderLink?> FindAsync(Guid accountId, string key, CancellationToken cancellationToken) =>
        db.OrderLinks.AsNoTracking().SingleOrDefaultAsync(l => l.AccountId == accountId && l.ClientRequestKey == key, cancellationToken);
}
