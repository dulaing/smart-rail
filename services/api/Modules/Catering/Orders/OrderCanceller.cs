using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Catering.Provider;

namespace SmartRail.Api.Modules.Catering.Orders;

public abstract record CancelResult
{
    public sealed record NotFound : CancelResult;

    public sealed record Cancelled(OrderLink Link) : CancelResult;

    // SmartRail or the provider refused with a stable code (cutoff, unknown train, wrong state).
    public sealed record Refused(string Code, string Detail) : CancelResult;

    // The provider could not be asked. Nothing changed; the passenger may try again.
    public sealed record ProviderUnavailable : CancelResult;
}

// Cancels one of the caller's own orders. SmartRail sends a fresh eligibility and ETA snapshot and
// Grilled decides, because the cutoff is the provider's and is never cached here.
public sealed class OrderCanceller(
    SmartRailDbContext db, PickupAssessor assessor, IGrilledClient provider, OrderLinkWrites writes, TimeProvider time)
{
    public async Task<CancelResult> CancelAsync(Guid accountId, Guid linkId, CancellationToken cancellationToken)
    {
        // Ownership is part of the query: another account's order is indistinguishable from none.
        var link = await db.OrderLinks.AsNoTracking()
            .SingleOrDefaultAsync(l => l.Id == linkId && l.AccountId == accountId, cancellationToken);
        if (link is null)
        {
            return new CancelResult.NotFound();
        }

        if (link.PlacementState == PlacementStates.Pending)
        {
            return new CancelResult.Refused("PLACEMENT_PENDING", "The order is still being placed. Try again shortly.");
        }

        if (link.PlacementState == PlacementStates.Rejected)
        {
            return new CancelResult.Refused("INVALID_STATE", "A refused order cannot be cancelled.");
        }

        if (link.ProviderStatus == "CANCELLED")
        {
            return new CancelResult.Cancelled(link);
        }

        var assessment = await assessor.AssessAsync(link.JourneyId, link.PickupStopId, cancellationToken);
        if (assessment.Journey is null || assessment.Pickup is null)
        {
            return new CancelResult.Refused(assessment.Problem ?? OrderProblems.JourneyNotFound, "The journey is no longer known.");
        }

        var response = await provider.CancelAsync(link.Id, new CancelOrderBody(assessment.Journey, assessment.Pickup), cancellationToken);
        switch (response.Outcome)
        {
            case ProviderOutcome.Success:
                await writes.ApplyProviderOrderAsync(link.Id, response.Value!, time.GetUtcNow(), cancellationToken);
                return new CancelResult.Cancelled(await db.OrderLinks.AsNoTracking().SingleAsync(l => l.Id == link.Id, cancellationToken));

            case ProviderOutcome.Refused:
                return new CancelResult.Refused(response.Error!.Code, response.Error.Message);

            default:
                return new CancelResult.ProviderUnavailable();
        }
    }
}
