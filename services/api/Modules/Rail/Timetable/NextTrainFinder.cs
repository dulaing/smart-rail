namespace SmartRail.Api.Modules.Rail.Timetable;

// A scheduled direct train from an origin stop to a later destination stop.
public sealed record ScheduledTrain(
    Guid JourneyId,
    DateOnly ServiceDate,
    string TrainNumber,
    SourceKind SourceKind,
    string? SourceUrl,
    DateTimeOffset? SourceCheckedAt,
    Guid OriginStopId,
    DateTimeOffset? OriginArrival,
    DateTimeOffset OriginDeparture,
    string? PlannedPlatform,
    Guid DestinationStopId,
    DateTimeOffset DestinationArrival);

// Picks the next direct scheduled trains between two stations. The caller decides which
// journeys to load (for an early-morning request, include the previous service date so an
// overnight train can match); this class only applies the matching rules.
public static class NextTrainFinder
{
    public const int DefaultLimit = 3;

    public static IReadOnlyList<ScheduledTrain> Find(
        IEnumerable<ScheduledJourney> journeys,
        Guid originStationId,
        Guid destinationStationId,
        DateTimeOffset at,
        int limit = DefaultLimit)
    {
        var trains = new List<ScheduledTrain>();

        foreach (var journey in journeys)
        {
            if (!journey.Pattern.RunsOn(journey.ServiceDate))
            {
                continue;
            }

            if (TryMatch(journey, originStationId, destinationStationId, at) is { } train)
            {
                trains.Add(train);
            }
        }

        return trains
            .OrderBy(t => t.OriginDeparture)
            .ThenBy(t => t.TrainNumber, StringComparer.Ordinal)
            .ThenBy(t => t.JourneyId)
            .Take(limit)
            .ToList();
    }

    // A route may visit a station twice, so match stop occurrences by sequence. The first
    // origin occurrence that has not yet departed and has a later destination wins, so a
    // journey is returned at most once.
    private static ScheduledTrain? TryMatch(
        ScheduledJourney journey,
        Guid originStationId,
        Guid destinationStationId,
        DateTimeOffset at)
    {
        // A train that has already arrived but not yet left is still catchable.
        var match = Matches(journey, originStationId, destinationStationId).FirstOrDefault(m => m.OriginDeparture >= at);
        if (match is null)
        {
            return null;
        }

        return new ScheduledTrain(
            journey.Id,
            journey.ServiceDate,
            journey.Pattern.TrainNumber,
            journey.Pattern.SourceKind,
            journey.Pattern.SourceUrl,
            journey.Pattern.SourceCheckedAt,
            match.Origin.Id,
            match.OriginArrival,
            match.OriginDeparture,
            match.Origin.PlannedPlatform,
            match.Destination.Id,
            match.DestinationArrival);
    }

    // Every origin occurrence of the journey that has a departure and a later destination
    // occurrence with an arrival, in running order and with no time filter. Each origin pairs with
    // the first destination after it. The live read uses this too, so it matches occurrences
    // exactly as the scheduled read does.
    public static IReadOnlyList<RouteMatch> Matches(
        ScheduledJourney journey, Guid originStationId, Guid destinationStationId)
    {
        var stops = journey.Pattern.Stops.OrderBy(s => s.Sequence).ToList();
        var matches = new List<RouteMatch>();

        for (var i = 0; i < stops.Count; i++)
        {
            var origin = stops[i];
            if (origin.StationId != originStationId || origin.DepartureLocal is not { } departureLocal)
            {
                continue;
            }

            for (var j = i + 1; j < stops.Count; j++)
            {
                var destination = stops[j];
                if (destination.StationId != destinationStationId || destination.ArrivalLocal is not { } arrivalLocal)
                {
                    continue;
                }

                var (originArrival, _) = ColomboTime.StopInstants(journey.ServiceDate, origin);
                matches.Add(new RouteMatch(
                    origin,
                    destination,
                    i,
                    j,
                    originArrival,
                    ColomboTime.ToInstant(journey.ServiceDate, departureLocal, origin.DayOffset),
                    ColomboTime.ToInstant(journey.ServiceDate, arrivalLocal, destination.DayOffset)));
                break;
            }
        }

        return matches;
    }
}

// One origin stop occurrence paired with the destination occurrence after it. The indexes are
// positions in the journey's stops ordered by sequence.
public sealed record RouteMatch(
    PatternStop Origin,
    PatternStop Destination,
    int OriginIndex,
    int DestinationIndex,
    DateTimeOffset? OriginArrival,
    DateTimeOffset OriginDeparture,
    DateTimeOffset DestinationArrival);
