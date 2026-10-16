namespace SmartRail.Api.Modules.Operations.Changes;

// The staff decisions in force for one journey, folded from its append-only history in
// journey-version order. Each decision holds one slot: DELAY, CANCEL and EARLY_END one each,
// PLATFORM one per stop occurrence. A newer publish replaces the slot's change; a withdrawal
// empties it and never brings back an older change that was already replaced.
public sealed class ConfirmedState
{
    private readonly Dictionary<(ChangeKind, Guid?), ServiceChange> slots;

    private ConfirmedState(Dictionary<(ChangeKind, Guid?), ServiceChange> slots)
    {
        this.slots = slots;
    }

    public ServiceChange? Delay => slots.GetValueOrDefault((ChangeKind.Delay, null));

    public ServiceChange? Cancel => slots.GetValueOrDefault((ChangeKind.Cancel, null));

    public ServiceChange? EarlyEnd => slots.GetValueOrDefault((ChangeKind.EarlyEnd, null));

    public ServiceChange? PlatformAt(Guid stopId) => slots.GetValueOrDefault((ChangeKind.Platform, stopId));

    // The publishes in force, oldest first.
    public IReadOnlyList<ServiceChange> Active => slots.Values.OrderBy(c => c.JourneyVersion).ToList();

    public bool IsActive(ServiceChange change) =>
        slots.GetValueOrDefault(SlotOf(change.Details)) is { } held && held.Id == change.Id;

    public static ConfirmedState Fold(IEnumerable<ServiceChange> history)
    {
        var ordered = history.OrderBy(c => c.JourneyVersion).ToList();
        var byId = ordered.ToDictionary(c => c.Id);
        var slots = new Dictionary<(ChangeKind, Guid?), ServiceChange>();

        foreach (var change in ordered)
        {
            if (change.Action == ChangeAction.Publish)
            {
                slots[SlotOf(change.Details)] = change;
                continue;
            }

            // A withdrawal clears only the change it names, and only while that change still
            // holds its slot. One that names nothing known, or a replaced change, does nothing.
            if (change.SupersedesChangeId is { } id
                && byId.TryGetValue(id, out var target)
                && target.Action == ChangeAction.Publish
                && slots.GetValueOrDefault(SlotOf(target.Details)) is { } held
                && held.Id == target.Id)
            {
                slots.Remove(SlotOf(target.Details));
            }
        }

        return new ConfirmedState(slots);
    }

    private static (ChangeKind, Guid?) SlotOf(ChangeDetails details) =>
        (details.Kind, details.Kind == ChangeKind.Platform ? details.TargetStopId : null);
}

public enum ServiceAvailability
{
    AsScheduled,
    EndsEarly,
    Cancelled,
}

public static class ServiceAvailabilities
{
    public static string ToCode(this ServiceAvailability availability) => availability switch
    {
        ServiceAvailability.AsScheduled => "AS_SCHEDULED",
        ServiceAvailability.EndsEarly => "ENDS_EARLY",
        ServiceAvailability.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(availability)),
    };
}

// One stop occurrence as passengers, boards and Catering should see it. Platform is the
// confirmed one while a platform change is in force, otherwise the scheduled one.
public sealed record StopOutcome(
    Guid StopId,
    Guid StationId,
    string StationName,
    int Sequence,
    bool Served,
    string? PlannedPlatform,
    string? ConfirmedPlatform,
    int? ConfirmedDelayMinutes)
{
    public string? Platform => ConfirmedPlatform ?? PlannedPlatform;
}

// A journey's confirmed picture: the schedule with the decisions in force applied. It holds no
// observation or prediction. A cancelled journey serves no stop, whatever else is in force.
// BlockedPickupStopIds is the later stops of an early ending, kept separate from the cancellation
// (OrderClosed) so withdrawing one leaves the other exactly as it was.
public sealed record JourneyOutcome(
    ServiceAvailability Availability,
    int? ConfirmedDelayMinutes,
    Guid? FinalStopId,
    IReadOnlyList<StopOutcome> Stops,
    IReadOnlyList<Guid> BlockedPickupStopIds)
{
    public bool OrderClosed => Availability == ServiceAvailability.Cancelled;

    public static JourneyOutcome From(OperationsJourney journey, ConfirmedState state)
    {
        var delay = state.Delay?.Details.DelayMinutes;
        var cancelled = state.Cancel is not null;
        var finalStopId = state.EarlyEnd?.Details.FinalStopId;

        // A final stop that is not in the journey cannot cut it short; treat it as no ending.
        var finalIndex = finalStopId is { } id ? journey.IndexOf(id) : -1;
        if (finalIndex < 0)
        {
            finalStopId = null;
        }

        var stops = journey.Stops.Select((stop, i) =>
        {
            var served = !cancelled && (finalIndex < 0 || i <= finalIndex);
            return new StopOutcome(
                stop.Id,
                stop.StationId,
                stop.StationName,
                stop.Sequence,
                served,
                stop.PlannedPlatform,
                state.PlatformAt(stop.Id)?.Details.NewPlatform,
                served ? delay : null);
        }).ToList();

        var blocked = finalIndex < 0
            ? []
            : journey.Stops.Skip(finalIndex + 1).Select(s => s.Id).ToList();

        var availability = cancelled
            ? ServiceAvailability.Cancelled
            : finalIndex < 0 ? ServiceAvailability.AsScheduled : ServiceAvailability.EndsEarly;

        return new JourneyOutcome(availability, delay, finalStopId, stops, blocked);
    }
}
