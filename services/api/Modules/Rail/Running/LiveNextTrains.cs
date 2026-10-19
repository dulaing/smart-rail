using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Rail.Running;

// A journey with what is known about it right now. The caller loads these; the rule below needs no
// database, clock or HTTP, so every case can be checked with fixed instants.
public sealed record LiveJourney(ScheduledJourney Journey, RunningObservation? Observation, ConfirmedOverrides Overrides);

// One eligible route match. The estimates are null unless the evidence is CURRENT or AGEING, and
// then the scheduled times above them are the only times to show, labelled as schedule. Overdue
// means the scheduled departure has passed and nothing proves the train left: it is still shown,
// but the client must not call it on time or catchable unless there is an estimate.
public sealed record LiveTrain(
    Guid JourneyId,
    DateOnly ServiceDate,
    string TrainNumber,
    TimetableSource Timetable,
    Guid OriginStopId,
    Guid DestinationStopId,
    DateTimeOffset? OriginScheduledArrival,
    DateTimeOffset OriginScheduledDeparture,
    DateTimeOffset? OriginEstimatedDeparture,
    DateTimeOffset DestinationScheduledArrival,
    DateTimeOffset? DestinationEstimatedArrival,
    string? PlannedPlatform,
    string? ConfirmedPlatform,
    DateTimeOffset? ConfirmedPlatformAt,
    string Freshness,
    DateTimeOffset? LastObservedAt,
    bool Overdue,
    bool EarliestArrival,
    string Availability,
    Guid? FinalStopId,
    int? ConfirmedDelayMinutes,
    DateTimeOffset? ConfirmedDelayAt,
    int? ConfirmedVersion);

// A confirmed cancellation or early ending that stops a matching train serving the route. The
// train is not offered as a choice; this keeps the news visible.
public sealed record LiveNotice(
    Guid JourneyId,
    DateOnly ServiceDate,
    string TrainNumber,
    string Kind,
    Guid OriginStopId,
    Guid DestinationStopId,
    DateTimeOffset OriginScheduledDeparture,
    Guid? FinalStopId,
    DateTimeOffset? ConfirmedAt,
    int? ConfirmedVersion);

public sealed record LiveNextTrainsResult(IReadOnlyList<LiveTrain> Trains, IReadOnlyList<LiveNotice> Notices);

public static class LiveNextTrains
{
    public const int DefaultLimit = NextTrainFinder.DefaultLimit;

    public const string Cancelled = "CANCELLED";
    public const string EndsEarly = "ENDS_EARLY";

    // Picks the trains a passenger could still take from the origin to the destination, from the whole
    // candidate set. Unlike the scheduled finder it does not drop a train because its scheduled
    // departure has passed:
    //  - a train stays until the newest observation proves it has left the origin occurrence
    //    (leaving is a fact that does not expire, so an old observation still proves it);
    //  - a train that is overdue with no usable estimate stays too, flagged Overdue, but only until
    //    its scheduled arrival at the destination (plus any confirmed delay) has passed. The spec
    //    leaves this end point open; without it a train nobody observed would be listed for days.
    // A cancelled train, or one that ends before the destination, is never a choice; it becomes a
    // notice. Ordering uses the estimated departure while the evidence supports one, else the schedule.
    public static LiveNextTrainsResult Find(
        IEnumerable<LiveJourney> journeys,
        Guid originStationId,
        Guid destinationStationId,
        DateTimeOffset now,
        int limit = DefaultLimit)
    {
        var trains = new List<LiveTrain>();
        var notices = new List<LiveNotice>();

        foreach (var live in journeys)
        {
            var journey = live.Journey;
            if (!journey.Pattern.RunsOn(journey.ServiceDate))
            {
                continue;
            }

            var matches = NextTrainFinder.Matches(journey, originStationId, destinationStationId);
            if (matches.Count == 0)
            {
                continue;
            }

            var state = RunningCalculator.Calculate(journey, live.Observation, now, overrides: live.Overrides);
            var orderedStops = journey.Pattern.Stops.OrderBy(s => s.Sequence).ToList();
            var leftBefore = StopsLeftBefore(orderedStops, live.Observation);

            foreach (var match in matches)
            {
                if (match.OriginIndex < leftBefore)
                {
                    continue;
                }

                var origin = state.Stops[match.OriginIndex];
                var destination = state.Stops[match.DestinationIndex];
                var delay = TimeSpan.FromMinutes(live.Overrides.DelayMinutes ?? 0);

                if (!origin.Served || !destination.Served)
                {
                    // Only worth telling someone who has not yet missed the train.
                    if (match.OriginDeparture + delay >= now)
                    {
                        notices.Add(NoticeFor(journey, match, state, live.Overrides));
                        break;
                    }

                    continue;
                }

                // Estimates exist only while the evidence is usable, so no estimate on the origin
                // means the train's progress is unknown and the schedule is all there is.
                var overdue = match.OriginDeparture < now && origin.EstimatedDeparture is null;
                if (overdue && now > match.DestinationArrival + delay)
                {
                    continue;
                }

                trains.Add(TrainFor(journey, match, state, origin, destination, live, overdue));
                break;
            }
        }

        var chosen = trains
            .OrderBy(t => t.OriginEstimatedDeparture ?? t.OriginScheduledDeparture)
            .ThenBy(t => t.OriginScheduledDeparture)
            .ThenBy(t => t.TrainNumber, StringComparer.Ordinal)
            .ThenBy(t => t.JourneyId)
            .Take(limit)
            .ToList();

        var sortedNotices = notices
            .OrderBy(n => n.OriginScheduledDeparture)
            .ThenBy(n => n.TrainNumber, StringComparer.Ordinal)
            .ThenBy(n => n.JourneyId)
            .Take(limit)
            .ToList();

        return new LiveNextTrainsResult(MarkEarliestArrival(chosen), sortedNotices);
    }

    // How many leading stop occurrences the train has already left, from the newest observation.
    // Below full progress the train has left the From stop; at full progress it stands at the To
    // stop and has left only those before it. Both mean "before the To stop". Evidence that does not
    // sit on neighbouring stops of this journey proves nothing.
    private static int StopsLeftBefore(IReadOnlyList<PatternStop> orderedStops, RunningObservation? observation)
    {
        if (observation is null || observation.ProgressPermille is < 0 or > RunningCalculator.FullProgress)
        {
            return 0;
        }

        return Math.Max(0, RunningCalculator.IndexOfSegmentEnd(orderedStops, observation.FromStopId, observation.ToStopId));
    }

    private static LiveTrain TrainFor(
        ScheduledJourney journey, RouteMatch match, RunningState state, StopRunning origin, StopRunning destination,
        LiveJourney live, bool overdue)
    {
        var pattern = journey.Pattern;
        var overrides = live.Overrides;
        return new LiveTrain(
            journey.Id,
            journey.ServiceDate,
            pattern.TrainNumber,
            new TimetableSource(pattern.SourceKind.ToCode(), pattern.SourceKind.Label(), pattern.SourceUrl, pattern.SourceCheckedAt),
            match.Origin.Id,
            match.Destination.Id,
            match.OriginArrival,
            match.OriginDeparture,
            origin.EstimatedDeparture,
            match.DestinationArrival,
            destination.EstimatedArrival,
            origin.PlannedPlatform,
            origin.ConfirmedPlatform,
            origin.ConfirmedPlatformAt,
            state.Freshness.ToCode(),
            live.Observation?.ObservedAt,
            overdue,
            false,
            state.Availability,
            state.FinalStopId,
            overrides.DelayMinutes,
            overrides.DelayConfirmedAt,
            overrides.Version);
    }

    private static LiveNotice NoticeFor(
        ScheduledJourney journey, RouteMatch match, RunningState state, ConfirmedOverrides overrides)
    {
        var cancelled = state.Availability == Availabilities.Cancelled;
        return new LiveNotice(
            journey.Id,
            journey.ServiceDate,
            journey.Pattern.TrainNumber,
            cancelled ? Cancelled : EndsEarly,
            match.Origin.Id,
            match.Destination.Id,
            match.OriginDeparture,
            cancelled ? null : state.FinalStopId,
            cancelled ? overrides.CancelledAt : overrides.EndsEarlyAt,
            overrides.Version);
    }

    // The earliest destination arrival is highlighted only when it can be compared fairly: two or
    // more choices, every one with a CURRENT evidence estimate, and one strictly earliest. Anything
    // less and no train is called the quickest.
    private static List<LiveTrain> MarkEarliestArrival(List<LiveTrain> trains)
    {
        if (trains.Count < 2
            || trains.Any(t => t.Freshness != Freshness.Current.ToCode() || t.DestinationEstimatedArrival is null))
        {
            return trains;
        }

        var earliest = trains.Min(t => t.DestinationEstimatedArrival);
        if (trains.Count(t => t.DestinationEstimatedArrival == earliest) != 1)
        {
            return trains;
        }

        return trains.Select(t => t.DestinationEstimatedArrival == earliest ? t with { EarliestArrival = true } : t).ToList();
    }
}
