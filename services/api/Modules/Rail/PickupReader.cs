using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Rail;

// One stop occurrence as Catering needs it to decide whether a pickup is possible. The estimate
// and the evidence time are both null while the position is unknown, and for a time that has
// already happened. Passed means the train has been seen leaving this stop.
public sealed record PickupStop(
    Guid StopId,
    Guid StationId,
    int Sequence,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? EstimatedArrival,
    DateTimeOffset? EtaEvidenceAt,
    bool Served,
    bool Passed);

// RunsToday is whether the journey's service date is the current date in Colombo.
public sealed record PickupJourney(
    Guid JourneyId, string TrainNumber, DateOnly ServiceDate, bool RunsToday, IReadOnlyList<PickupStop> Stops);

public enum PassageState
{
    Ahead,
    Passed,
    Unknown,
}

// The small door Catering uses to read when a train reaches each stop. Catering never reads Rail's
// tables or runs its calculation.
public interface IPickupReader
{
    // The journey's stops as they would be after `overrides`, or as confirmed now when none are
    // given. Null for an unknown journey.
    Task<PickupJourney?> ReadAsync(
        Guid journeyId, DateTimeOffset now, ConfirmedOverrides? overrides, CancellationToken cancellationToken);

    // Whether the train has left a stop occurrence, from the newest observation. Departure is a
    // fact that does not expire, so an old observation still proves it; a missing one proves nothing.
    Task<PassageState?> ReadPassageAsync(Guid journeyId, Guid stopId, CancellationToken cancellationToken);
}

public sealed class PickupReader(SmartRailDbContext db, IConfirmedChangeReader confirmed) : IPickupReader
{
    public async Task<PickupJourney?> ReadAsync(
        Guid journeyId, DateTimeOffset now, ConfirmedOverrides? overrides, CancellationToken cancellationToken)
    {
        var journey = await LoadAsync(journeyId, cancellationToken);
        if (journey is null)
        {
            return null;
        }

        var observation = await ObservationIngestion.CurrentAsync(db, journeyId, cancellationToken);
        overrides ??= await confirmed.ReadAsync(journeyId, cancellationToken);
        var state = RunningCalculator.Calculate(journey, observation?.ToRunning(), now, overrides: overrides);

        var order = journey.Pattern.Stops.OrderBy(s => s.Sequence).Select(s => s.Id).ToList();
        var lastSeen = observation?.ToRunning();
        var stops = state.Stops.Select(stop => new PickupStop(
            stop.StopId,
            stop.StationId,
            stop.Sequence,
            stop.ScheduledArrival,
            stop.EstimatedArrival,
            // The estimate rests on the observation, so its evidence time is that observation's.
            stop.EstimatedArrival is null ? null : lastSeen?.ObservedAt,
            stop.Served,
            HasLeft(order, lastSeen, stop.StopId))).ToList();

        return new PickupJourney(
            journeyId, journey.Pattern.TrainNumber, journey.ServiceDate, journey.ServiceDate == ColomboTime.ServiceDateOf(now), stops);
    }

    public async Task<PassageState?> ReadPassageAsync(Guid journeyId, Guid stopId, CancellationToken cancellationToken)
    {
        var journey = await LoadAsync(journeyId, cancellationToken);
        if (journey is null || journey.Pattern.Stops.All(s => s.Id != stopId))
        {
            return null;
        }

        var observation = await ObservationIngestion.CurrentAsync(db, journeyId, cancellationToken);
        if (observation is null)
        {
            return PassageState.Unknown;
        }

        var order = journey.Pattern.Stops.OrderBy(s => s.Sequence).Select(s => s.Id).ToList();
        return HasLeft(order, observation.ToRunning(), stopId) ? PassageState.Passed : PassageState.Ahead;
    }

    // On a segment below full progress the train has left the From stop and everything before it.
    // At full progress it stands at the To stop, so only the stops before that one are behind it.
    private static bool HasLeft(IReadOnlyList<Guid> orderedStopIds, RunningObservation? observation, Guid stopId)
    {
        if (observation is null)
        {
            return false;
        }

        var index = orderedStopIds.ToList().IndexOf(stopId);
        var from = orderedStopIds.ToList().IndexOf(observation.FromStopId);
        var to = orderedStopIds.ToList().IndexOf(observation.ToStopId);
        if (index < 0 || from < 0 || to < 0)
        {
            return false;
        }

        return observation.ProgressPermille < RunningCalculator.FullProgress ? index <= from : index < to;
    }

    private Task<ScheduledJourney?> LoadAsync(Guid journeyId, CancellationToken cancellationToken) =>
        db.Journeys.AsNoTracking()
            .Include(j => j.Pattern).ThenInclude(p => p.Stops)
            .SingleOrDefaultAsync(j => j.Id == journeyId, cancellationToken);
}
