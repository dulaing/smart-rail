using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Catering.Provider;

namespace SmartRail.Api.Modules.Catering.Orders;

// Every change to a link goes through one conditional UPDATE, so two writers (the request that
// placed the order, the retry loop, later a callback) cannot step on each other: whoever arrives
// second finds the row already moved on and changes nothing.
public sealed class OrderLinkWrites(SmartRailDbContext db)
{
    // Takes the provider's order as the truth for the link, unless the link already holds a newer
    // status. Returns whether anything changed. Also what a status callback will use.
    public async Task<bool> ApplyProviderOrderAsync(
        Guid linkId, ProviderOrder order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var items = JsonSerializer.Serialize(order.Items, ProviderJson.Options);
        var rows = await db.OrderLinks
            .Where(l => l.Id == linkId
                && order.OrderLinkId == linkId
                && (l.PlacementState != PlacementStates.Confirmed || l.StatusSequence < order.StatusSequence))
            .ExecuteUpdateAsync(set => set
                .SetProperty(l => l.PlacementState, PlacementStates.Confirmed)
                .SetProperty(l => l.RejectionCode, (string?)null)
                .SetProperty(l => l.RequestPayload, (string?)null)
                .SetProperty(l => l.ProviderOrderRef, order.Id)
                .SetProperty(l => l.ProviderStatus, order.Status)
                .SetProperty(l => l.StatusSequence, order.StatusSequence)
                .SetProperty(l => l.CancellationReason, order.CancellationReason)
                .SetProperty(l => l.PickupCode, order.PickupCode)
                .SetProperty(l => l.ItemsJson, items)
                .SetProperty(l => l.ScheduledArrival, order.PickupTime.ScheduledArrival)
                .SetProperty(l => l.EstimatedArrival, order.PickupTime.EstimatedArrival)
                .SetProperty(l => l.EtaEvidenceAt, order.PickupTime.EtaEvidenceAt)
                .SetProperty(l => l.LastError, (string?)null)
                .SetProperty(l => l.UpdatedAt, now), cancellationToken);
        return rows > 0;
    }

    // A final refusal. Only a link still waiting can be refused, and its request JSON is cleared.
    public async Task<bool> RejectAsync(Guid linkId, string code, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.OrderLinks
            .Where(l => l.Id == linkId && l.PlacementState == PlacementStates.Pending)
            .ExecuteUpdateAsync(set => set
                .SetProperty(l => l.PlacementState, PlacementStates.Rejected)
                .SetProperty(l => l.RejectionCode, code)
                .SetProperty(l => l.RequestPayload, (string?)null)
                .SetProperty(l => l.LastError, (string?)null)
                .SetProperty(l => l.UpdatedAt, now), cancellationToken) > 0;

    // Recorded before a placement is sent, so a crash mid-call still shows an attempt was made.
    public Task MarkAttemptAsync(Guid linkId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.OrderLinks
            .Where(l => l.Id == linkId && l.PlacementState == PlacementStates.Pending)
            .ExecuteUpdateAsync(set => set
                .SetProperty(l => l.AttemptCount, l => l.AttemptCount + 1)
                .SetProperty(l => l.LastAttemptAt, now)
                .SetProperty(l => l.UpdatedAt, now), cancellationToken);

    // Keeps the link waiting and says when to look at it again.
    public Task DeferAsync(Guid linkId, DateTimeOffset next, string? reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.OrderLinks
            .Where(l => l.Id == linkId && l.PlacementState == PlacementStates.Pending)
            .ExecuteUpdateAsync(set => set
                .SetProperty(l => l.NextAttemptAt, next)
                .SetProperty(l => l.LastError, reason)
                .SetProperty(l => l.UpdatedAt, now), cancellationToken);
}
