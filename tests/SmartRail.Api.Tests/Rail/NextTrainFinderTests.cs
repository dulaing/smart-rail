using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Tests.Rail;

public class NextTrainFinderTests
{
    private static readonly Guid Gampaha = Guid.NewGuid();
    private static readonly Guid Ragama = Guid.NewGuid();
    private static readonly Guid Fort = Guid.NewGuid();

    // 2026-10-14 is a Wednesday.
    private static readonly DateOnly Wednesday = new(2026, 10, 14);
    private static readonly DateOnly Thursday = new(2026, 10, 15);

    private static DateTimeOffset Colombo(DateOnly date, int hour, int minute, int second = 0) =>
        new(date.ToDateTime(new TimeOnly(hour, minute, second)), ColomboTime.Offset);

    private static PatternStop Stop(
        Guid station, int sequence, string? arrival, string? departure, int dayOffset = 0, string? platform = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            StationId = station,
            Sequence = sequence,
            ArrivalLocal = arrival is null ? null : TimeOnly.Parse(arrival),
            DepartureLocal = departure is null ? null : TimeOnly.Parse(departure),
            DayOffset = dayOffset,
            PlannedPlatform = platform,
        };

    private static ServicePattern Pattern(
        string trainNumber,
        PatternStop[] stops,
        OperatingDays days = OperatingDays.Every,
        DateOnly? from = null,
        DateOnly? to = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TrainNumber = trainNumber,
            OperatingDays = days,
            EffectiveFrom = from ?? new DateOnly(2026, 1, 1),
            EffectiveTo = to,
            Stops = [.. stops],
        };

    private static ScheduledJourney Journey(ServicePattern pattern, DateOnly date) =>
        new() { Id = Guid.NewGuid(), PatternId = pattern.Id, ServiceDate = date, Pattern = pattern };

    // Gampaha 08:00 -> Ragama 08:10 -> Fort 08:40
    private static ServicePattern GampahaToFort(string trainNumber = "1", OperatingDays days = OperatingDays.Every,
        DateOnly? from = null, DateOnly? to = null) =>
        Pattern(trainNumber,
        [
            Stop(Gampaha, 1, null, "08:00", platform: "2"),
            Stop(Ragama, 2, "08:10", "08:11"),
            Stop(Fort, 3, "08:40", null),
        ], days, from, to);

    [Fact]
    public void Returns_a_train_that_leaves_later_and_serves_the_destination()
    {
        var journey = Journey(GampahaToFort(), Wednesday);

        var result = NextTrainFinder.Find([journey], Gampaha, Fort, Colombo(Wednesday, 7, 30));

        var train = Assert.Single(result);
        Assert.Equal(journey.Id, train.JourneyId);
        Assert.Equal(Colombo(Wednesday, 8, 0), train.OriginDeparture);
        Assert.Equal(Colombo(Wednesday, 8, 40), train.DestinationArrival);
        Assert.Equal("2", train.PlannedPlatform);
        Assert.Null(train.OriginArrival);
    }

    [Fact]
    public void Departure_at_exactly_the_requested_instant_is_included_and_one_second_earlier_is_not()
    {
        var journey = Journey(GampahaToFort(), Wednesday);

        var exact = NextTrainFinder.Find([journey], Gampaha, Fort, Colombo(Wednesday, 8, 0));
        var late = NextTrainFinder.Find([journey], Gampaha, Fort, Colombo(Wednesday, 8, 0, 1));

        Assert.Single(exact);
        Assert.Empty(late);
    }

    [Fact]
    public void Train_that_has_arrived_but_not_yet_departed_is_still_eligible()
    {
        var journey = Journey(GampahaToFort(), Wednesday);

        // Ragama: arrives 08:10, departs 08:11. At 08:10:30 the arrival has passed.
        var result = NextTrainFinder.Find([journey], Ragama, Fort, Colombo(Wednesday, 8, 10, 30));

        var train = Assert.Single(result);
        Assert.Equal(Colombo(Wednesday, 8, 10), train.OriginArrival);
        Assert.Equal(Colombo(Wednesday, 8, 11), train.OriginDeparture);
    }

    [Fact]
    public void Reverse_route_matches_nothing_on_a_one_way_pattern()
    {
        var journey = Journey(GampahaToFort(), Wednesday);

        Assert.Empty(NextTrainFinder.Find([journey], Fort, Gampaha, Colombo(Wednesday, 0, 0)));
    }

    [Fact]
    public void Reverse_pattern_serves_the_reverse_route()
    {
        var back = Pattern("2",
        [
            Stop(Fort, 1, null, "09:00"),
            Stop(Gampaha, 2, "09:40", null),
        ]);

        var result = NextTrainFinder.Find([Journey(back, Wednesday)], Fort, Gampaha, Colombo(Wednesday, 8, 0));

        Assert.Equal(Colombo(Wednesday, 9, 40), Assert.Single(result).DestinationArrival);
    }

    [Fact]
    public void Weekday_filter_skips_a_pattern_that_does_not_run_that_day()
    {
        var weekdays = GampahaToFort(days: OperatingDays.Monday | OperatingDays.Tuesday | OperatingDays.Wednesday
            | OperatingDays.Thursday | OperatingDays.Friday);
        var saturday = new DateOnly(2026, 10, 17);

        Assert.Single(NextTrainFinder.Find([Journey(weekdays, Wednesday)], Gampaha, Fort, Colombo(Wednesday, 7, 0)));
        Assert.Empty(NextTrainFinder.Find([Journey(weekdays, saturday)], Gampaha, Fort, Colombo(saturday, 7, 0)));
    }

    [Theory]
    [InlineData(2026, 10, 12, true)]   // Monday
    [InlineData(2026, 10, 18, true)]   // Sunday
    [InlineData(2026, 10, 13, false)]  // Tuesday
    public void Operating_day_mask_uses_monday_as_the_first_bit(int year, int month, int day, bool runs)
    {
        var pattern = GampahaToFort(days: OperatingDays.Monday | OperatingDays.Sunday);

        Assert.Equal(runs, pattern.RunsOn(new DateOnly(year, month, day)));
    }

    [Fact]
    public void Effective_dates_are_inclusive_and_an_open_end_has_no_limit()
    {
        var bounded = GampahaToFort(from: Wednesday, to: Thursday);
        var open = GampahaToFort(from: Wednesday);

        Assert.False(bounded.RunsOn(Wednesday.AddDays(-1)));
        Assert.True(bounded.RunsOn(Wednesday));
        Assert.True(bounded.RunsOn(Thursday));
        Assert.False(bounded.RunsOn(Thursday.AddDays(1)));
        Assert.True(open.RunsOn(Wednesday.AddYears(5)));
    }

    [Fact]
    public void Journey_outside_the_effective_dates_is_ignored()
    {
        var expired = GampahaToFort(to: Wednesday.AddDays(-1));

        Assert.Empty(NextTrainFinder.Find([Journey(expired, Wednesday)], Gampaha, Fort, Colombo(Wednesday, 7, 0)));
    }

    [Fact]
    public void Early_morning_request_matches_an_overnight_journey_from_the_previous_service_date()
    {
        // Service date Wednesday; Fort 23:50, Gampaha 00:40 on Thursday (day offset 1).
        var overnight = Pattern("night",
        [
            Stop(Fort, 1, null, "23:50"),
            Stop(Gampaha, 2, "00:40", null, dayOffset: 1),
        ]);
        var journey = Journey(overnight, Wednesday);

        var result = NextTrainFinder.Find([journey], Fort, Gampaha, Colombo(Wednesday, 23, 0));

        var train = Assert.Single(result);
        Assert.Equal(Colombo(Wednesday, 23, 50), train.OriginDeparture);
        Assert.Equal(Colombo(Thursday, 0, 40), train.DestinationArrival);
    }

    [Fact]
    public void Overnight_origin_after_midnight_belongs_to_the_previous_service_date()
    {
        // Boards at Ragama at 00:10 on Thursday, which is still Wednesday's service.
        var overnight = Pattern("night",
        [
            Stop(Gampaha, 1, null, "23:40"),
            Stop(Ragama, 2, "00:08", "00:10", dayOffset: 1),
            Stop(Fort, 3, "00:40", null, dayOffset: 1),
        ]);
        var wednesdayRun = Journey(overnight, Wednesday);
        var thursdayRun = Journey(overnight, Thursday);

        var result = NextTrainFinder.Find(
            [thursdayRun, wednesdayRun], Ragama, Fort, Colombo(Thursday, 0, 5));

        var train = Assert.Single(result, t => t.OriginDeparture == Colombo(Thursday, 0, 10));
        Assert.Equal(wednesdayRun.Id, train.JourneyId);
        Assert.Equal(Wednesday, train.ServiceDate);
        Assert.Equal(Colombo(Thursday, 0, 8), train.OriginArrival);
    }

    [Fact]
    public void Arrival_written_after_the_departure_at_one_stop_is_the_day_before()
    {
        // Arrives 23:58, leaves 00:03 the next day; both share day offset 1.
        var pattern = Pattern("night",
        [
            Stop(Gampaha, 1, null, "23:30"),
            Stop(Ragama, 2, "23:58", "00:03", dayOffset: 1),
            Stop(Fort, 3, "00:30", null, dayOffset: 1),
        ]);

        var result = NextTrainFinder.Find([Journey(pattern, Wednesday)], Ragama, Fort, Colombo(Wednesday, 23, 0));

        var train = Assert.Single(result);
        Assert.Equal(Colombo(Wednesday, 23, 58), train.OriginArrival);
        Assert.Equal(Colombo(Thursday, 0, 3), train.OriginDeparture);
    }

    [Fact]
    public void Repeated_station_uses_the_first_departure_that_has_a_later_destination_once()
    {
        // Gampaha 08:00, Fort 08:40 (arrive/leave 08:45), Gampaha 09:30 (arrive/leave 09:35), Ragama 09:45.
        var loop = Pattern("loop",
        [
            Stop(Gampaha, 1, null, "08:00"),
            Stop(Fort, 2, "08:40", "08:45"),
            Stop(Gampaha, 3, "09:30", "09:35"),
            Stop(Ragama, 4, "09:45", null),
        ]);
        var journey = Journey(loop, Wednesday);

        // Both Gampaha occurrences leave after 07:00; the first one has Fort later.
        var toFort = Assert.Single(NextTrainFinder.Find([journey], Gampaha, Fort, Colombo(Wednesday, 7, 0)));
        Assert.Equal(Colombo(Wednesday, 8, 0), toFort.OriginDeparture);

        // After 08:00 only the second occurrence is left and it has Ragama later.
        var toRagama = Assert.Single(NextTrainFinder.Find([journey], Gampaha, Ragama, Colombo(Wednesday, 8, 1)));
        Assert.Equal(Colombo(Wednesday, 9, 35), toRagama.OriginDeparture);

        // Gampaha is only a destination before the second occurrence; Fort -> Gampaha uses stop 3.
        var back = Assert.Single(NextTrainFinder.Find([journey], Fort, Gampaha, Colombo(Wednesday, 8, 0)));
        Assert.Equal(Colombo(Wednesday, 9, 30), back.DestinationArrival);
    }

    [Fact]
    public void A_stop_that_is_visited_before_the_origin_cannot_be_the_destination()
    {
        var pattern = Pattern("p",
        [
            Stop(Fort, 1, null, "08:00"),
            Stop(Gampaha, 2, "08:40", null),
        ]);

        Assert.Empty(NextTrainFinder.Find([Journey(pattern, Wednesday)], Gampaha, Fort, Colombo(Wednesday, 7, 0)));
    }

    [Fact]
    public void Returns_at_most_three_sorted_by_departure_with_a_stable_tie_breaker()
    {
        var journeys = new[]
        {
            Journey(GampahaToFort("40"), Wednesday),
            Journey(Pattern("10",
            [
                Stop(Gampaha, 1, null, "07:30"),
                Stop(Fort, 2, "08:10", null),
            ]), Wednesday),
            Journey(GampahaToFort("20"), Wednesday),
            Journey(Pattern("50",
            [
                Stop(Gampaha, 1, null, "09:00"),
                Stop(Fort, 2, "09:40", null),
            ]), Wednesday),
        };

        var result = NextTrainFinder.Find(journeys, Gampaha, Fort, Colombo(Wednesday, 7, 0));

        Assert.Equal(["10", "20", "40"], result.Select(t => t.TrainNumber));
    }

    [Fact]
    public void Unknown_route_returns_an_empty_list()
    {
        var journey = Journey(GampahaToFort(), Wednesday);

        Assert.Empty(NextTrainFinder.Find([journey], Gampaha, Guid.NewGuid(), Colombo(Wednesday, 7, 0)));
        Assert.Empty(NextTrainFinder.Find([], Gampaha, Fort, Colombo(Wednesday, 7, 0)));
    }

    [Fact]
    public void Stops_are_matched_by_sequence_even_when_supplied_out_of_order()
    {
        var shuffled = Pattern("p",
        [
            Stop(Fort, 3, "08:40", null),
            Stop(Gampaha, 1, null, "08:00"),
            Stop(Ragama, 2, "08:10", "08:11"),
        ]);

        Assert.Single(NextTrainFinder.Find([Journey(shuffled, Wednesday)], Gampaha, Fort, Colombo(Wednesday, 7, 0)));
    }
}
