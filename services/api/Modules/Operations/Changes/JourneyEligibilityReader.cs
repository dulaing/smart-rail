using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Operations.Changes;

// What the confirmed changes mean for food ordering on one journey, at one journey version:
// whether the whole journey is cancelled and which pickup stops it no longer serves.
// Overrides is the same decisions in the form Rail needs to calculate pickup times from them.
public sealed record JourneyEligibility(
    int Version, bool OrderClosed, IReadOnlyList<Guid> BlockedPickupStopIds, ConfirmedOverrides Overrides)
{
    public bool Serves(Guid stopId) => !OrderClosed && !BlockedPickupStopIds.Contains(stopId);
}

// The small door Catering uses to ask Operations about ordering eligibility. Catering never reads
// Operations' tables or folds the change history itself.
public interface IJourneyEligibilityReader
{
    // Null for an unknown journey. A journey with no changes is version 0 with nothing blocked.
    Task<JourneyEligibility?> ReadAsync(Guid journeyId, CancellationToken cancellationToken);

    // The same, as it stood at an earlier journey version (the changes made up to and including it).
    // Used to rebuild a message that was queued before pickup times existed.
    Task<JourneyEligibility?> ReadAtVersionAsync(Guid journeyId, int version, CancellationToken cancellationToken);
}

public sealed class JourneyEligibilityReader(ChangeStore store, IJourneyStopReader journeys) : IJourneyEligibilityReader
{
    public Task<JourneyEligibility?> ReadAsync(Guid journeyId, CancellationToken cancellationToken) =>
        ReadAsync(journeyId, null, cancellationToken);

    public Task<JourneyEligibility?> ReadAtVersionAsync(Guid journeyId, int version, CancellationToken cancellationToken) =>
        ReadAsync(journeyId, version, cancellationToken);

    private async Task<JourneyEligibility?> ReadAsync(Guid journeyId, int? atVersion, CancellationToken cancellationToken)
    {
        var journey = await journeys.FindOperationsJourneyAsync(journeyId, cancellationToken);
        if (journey is null)
        {
            return null;
        }

        var loaded = await store.LoadAsync(journeyId, cancellationToken);
        if (loaded is null)
        {
            return new JourneyEligibility(0, false, [], ConfirmedOverrides.None);
        }

        var version = Math.Min(loaded.Version, atVersion ?? loaded.Version);
        var state = ConfirmedState.Fold(loaded.History.Where(c => c.JourneyVersion <= version));
        var outcome = JourneyOutcome.From(journey, state);
        return new JourneyEligibility(version, outcome.OrderClosed, outcome.BlockedPickupStopIds, ConfirmedChangeReader.From(state));
    }
}
