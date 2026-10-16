using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Rail.Running;

// Turns a dated journey, its newest observation and the current time into the running
// state. No database, clock or HTTP: the caller supplies the time, so every rule can be
// checked with fixed instants.
public static class RunningCalculator
{
    public const int FullProgress = 1000;

    public static RunningState Calculate(
        ScheduledJourney journey,
        RunningObservation? observation,
        DateTimeOffset now,
        int? confirmedDelaySeconds = null)
    {
        var stops = journey.Pattern.Stops.OrderBy(s => s.Sequence).ToList();
        var times = stops.Select(s => ColomboTime.StopInstants(journey.ServiceDate, s)).ToList();

        var freshness = FreshnessRules.Classify(observation?.ObservedAt, now);
        var toIndex = observation is null ? -1 : IndexOfToStop(stops, observation);

        // Evidence that cannot be placed on this journey is treated as no evidence.
        if (freshness == Freshness.Unknown || observation is null || toIndex < 0)
        {
            var scheduleOnly = stops.Select((s, i) => Row(s, times[i], null, null)).ToList();
            return new RunningState(Freshness.Unknown, observation, null, null, scheduleOnly);
        }

        // Below 1000 the train is heading for the To stop; at 1000 it is there and the
        // following occurrence is next.
        var atStop = observation.ProgressPermille == FullProgress;
        var nextIndex = atStop ? toIndex + 1 : toIndex;

        var delay = DelaySeconds(observation, confirmedDelaySeconds, nextIndex < stops.Count ? times[nextIndex] : null, now);
        var delayBy = TimeSpan.FromSeconds(delay);

        // A train standing at a stop has still to depart from it; every other earlier stop is done.
        var firstDeparture = atStop ? toIndex : nextIndex;

        var rows = stops.Select((s, i) => Row(
            s,
            times[i],
            i >= nextIndex ? times[i].Arrival + delayBy : null,
            i >= firstDeparture ? times[i].Departure + delayBy : null)).ToList();

        var position = new CurrentPosition(observation.FromStopId, observation.ToStopId, observation.ProgressPermille);
        return new RunningState(freshness, observation, position, delay, rows);
    }

    // Where a segment ends in the ordered stops, or -1 when the two are not neighbouring
    // occurrences. Adjacent means next in order, whatever the sequence numbers are.
    public static int IndexOfSegmentEnd(IReadOnlyList<PatternStop> orderedStops, Guid fromStopId, Guid toStopId)
    {
        for (var i = 1; i < orderedStops.Count; i++)
        {
            if (orderedStops[i].Id == toStopId)
            {
                return orderedStops[i - 1].Id == fromStopId ? i : -1;
            }
        }

        return -1;
    }

    private static int IndexOfToStop(List<PatternStop> stops, RunningObservation observation) =>
        observation.ProgressPermille is < 0 or > FullProgress
            ? -1
            : IndexOfSegmentEnd(stops, observation.FromStopId, observation.ToStopId);

    // The greatest of: nothing, the reported delay, a staff-confirmed delay, and how overdue
    // the next stop already is. Never negative, so a train is never estimated early.
    private static int DelaySeconds(
        RunningObservation observation,
        int? confirmedDelaySeconds,
        (DateTimeOffset? Arrival, DateTimeOffset? Departure)? next,
        DateTimeOffset now)
    {
        var overdue = 0;
        if (next is { } t && (t.Arrival ?? t.Departure) is { } due && now > due)
        {
            overdue = (int)Math.Floor((now - due).TotalSeconds);
        }

        return Math.Max(Math.Max(0, observation.ReportedDelaySeconds), Math.Max(confirmedDelaySeconds ?? 0, overdue));
    }

    private static StopRunning Row(
        PatternStop stop,
        (DateTimeOffset? Arrival, DateTimeOffset? Departure) time,
        DateTimeOffset? estimatedArrival,
        DateTimeOffset? estimatedDeparture) =>
        new(
            stop.Id,
            stop.StationId,
            stop.Sequence,
            time.Arrival,
            time.Departure,
            stop.PlannedPlatform,
            estimatedArrival,
            estimatedDeparture);
}
