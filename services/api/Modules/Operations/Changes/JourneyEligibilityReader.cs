using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Operations.Changes;

// What the confirmed changes mean for food ordering on one journey, at one journey version:
// whether the whole journey is cancelled and which pickup stops it no longer serves.
public sealed record JourneyEligibility(int Version, bool OrderClosed, IReadOnlyList<Guid> BlockedPickupStopIds)
{
    public bool Serves(Guid stopId) => !OrderClosed && !BlockedPickupStopIds.Contains(stopId);
}

// The small door Catering uses to ask Operations about ordering eligibility. Catering never reads
// Operations' tables or folds the change history itself.
public interface IJourneyEligibilityReader
{
    // Null for an unknown journey. A journey with no changes is version 0 with nothing blocked.
    Task<JourneyEligibility?> ReadAsync(Guid journeyId, CancellationToken cancellationToken);
}

public sealed class JourneyEligibilityReader(ChangeStore store, IJourneyStopReader journeys) : IJourneyEligibilityReader
{
    public async Task<JourneyEligibility?> ReadAsync(Guid journeyId, CancellationToken cancellationToken)
    {
        var journey = await journeys.FindOperationsJourneyAsync(journeyId, cancellationToken);
        if (journey is null)
        {
            return null;
        }

        var loaded = await store.LoadAsync(journeyId, cancellationToken);
        if (loaded is null)
        {
            return new JourneyEligibility(0, false, []);
        }

        var outcome = JourneyOutcome.From(journey, ConfirmedState.Fold(loaded.History));
        return new JourneyEligibility(loaded.Version, outcome.OrderClosed, outcome.BlockedPickupStopIds);
    }
}
