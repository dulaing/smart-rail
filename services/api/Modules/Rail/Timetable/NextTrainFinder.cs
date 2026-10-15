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
        var stops = journey.Pattern.Stops.OrderBy(s => s.Sequence).ToList();

        for (var i = 0; i < stops.Count; i++)
        {
            var origin = stops[i];
            if (origin.StationId != originStationId || origin.DepartureLocal is not { } departureLocal)
            {
                continue;
            }

            // A train that has already arrived but not yet left is still catchable.
            var departure = ColomboTime.ToInstant(journey.ServiceDate, departureLocal, origin.DayOffset);
            if (departure < at)
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

                // One day offset serves both times, so an arrival "after" the departure
                // (23:58 in, 00:03 out) happened the day before.
                DateTimeOffset? originArrival = null;
                if (origin.ArrivalLocal is { } a)
                {
                    originArrival = ColomboTime.ToInstant(journey.ServiceDate, a, origin.DayOffset);
                    if (originArrival > departure)
                    {
                        originArrival = originArrival.Value.AddDays(-1);
                    }
                }

                return new ScheduledTrain(
                    journey.Id,
                    journey.ServiceDate,
                    journey.Pattern.TrainNumber,
                    journey.Pattern.SourceKind,
                    journey.Pattern.SourceUrl,
                    journey.Pattern.SourceCheckedAt,
                    origin.Id,
                    originArrival,
                    departure,
                    origin.PlannedPlatform,
                    destination.Id,
                    ColomboTime.ToInstant(journey.ServiceDate, arrivalLocal, destination.DayOffset));
            }
        }

        return null;
    }
}
