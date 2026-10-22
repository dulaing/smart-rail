using SmartRail.Api.Modules.Catering.Provider;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Catering.Orders;

// The reasons SmartRail itself can see that a pickup is not available. The provider adds its own
// (cutoff, stock, items); those come back from Grilled unchanged.
public static class OrderProblems
{
    public const string JourneyNotFound = "JOURNEY_NOT_FOUND";
    public const string StopNotFound = "STOP_NOT_FOUND";
    public const string JourneyNotToday = "JOURNEY_NOT_TODAY";
    public const string StopNotServed = "STOP_NOT_SERVED";
    public const string StopPassed = "STOP_PASSED";
    public const string TrainUnknown = "TRAIN_UNKNOWN";
}

// What SmartRail knows about one pickup right now. Journey and Pickup are the snapshot Grilled
// receives; they exist whenever the journey and stop do, even if Problem says a new order is not
// possible, because a cancellation or a reconciliation still sends them.
public sealed record PickupAssessment(JourneySnapshot? Journey, PickupSnapshot? Pickup, string? Problem);

// Combines Rail's timing and Operations' eligibility into the one answer Catering needs. It uses
// only their named interfaces. Nothing here comes from the passenger.
public sealed class PickupAssessor(IPickupReader pickups, IJourneyEligibilityReader eligibility, TimeProvider time)
{
    public async Task<PickupAssessment> AssessAsync(Guid journeyId, Guid stopId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var journey = await pickups.ReadAsync(journeyId, now, null, cancellationToken);
        var rules = await eligibility.ReadAsync(journeyId, cancellationToken);
        if (journey is null || rules is null)
        {
            return new PickupAssessment(null, null, OrderProblems.JourneyNotFound);
        }

        var stop = journey.Stops.SingleOrDefault(s => s.StopId == stopId);
        if (stop is null || stop.ScheduledArrival is null)
        {
            return new PickupAssessment(null, null, OrderProblems.StopNotFound);
        }

        return new PickupAssessment(
            SnapshotOf(journey, rules),
            new PickupSnapshot(stop.StopId, stop.StationId, stop.ScheduledArrival.Value, stop.EstimatedArrival, stop.EtaEvidenceAt),
            ProblemFor(journey, rules, stop));
    }

    // The full-eligibility snapshot Grilled needs, from the journey's current confirmed state.
    public static JourneySnapshot SnapshotOf(PickupJourney journey, JourneyEligibility rules) =>
        new(journey.JourneyId, journey.TrainNumber, journey.ServiceDate, rules.Version, rules.OrderClosed, rules.BlockedPickupStopIds);

    // Offer only a served stop still ahead of the train, on a journey dated today in Colombo,
    // with a usable current or ageing arrival estimate. Anything else is a reason, not an offer.
    public static string? ProblemFor(PickupJourney journey, JourneyEligibility rules, PickupStop stop)
    {
        if (!journey.RunsToday)
        {
            return OrderProblems.JourneyNotToday;
        }

        if (!rules.Serves(stop.StopId) || !stop.Served)
        {
            return OrderProblems.StopNotServed;
        }

        if (stop.Passed)
        {
            return OrderProblems.StopPassed;
        }

        return stop.EstimatedArrival is null ? OrderProblems.TrainUnknown : null;
    }
}
