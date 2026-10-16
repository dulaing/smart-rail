using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Tests.Rail;

// Freshness and ETA rules with fixed instants: no database, clock or sleeping.
public class RunningCalculatorTests
{
    private static readonly Guid GampahaStation = Guid.NewGuid();
    private static readonly Guid RagamaStation = Guid.NewGuid();
    private static readonly Guid FortStation = Guid.NewGuid();

    // 2026-10-14 is a Wednesday.
    private static readonly DateOnly Wednesday = new(2026, 10, 14);
    private static readonly DateOnly Thursday = new(2026, 10, 15);

    private static DateTimeOffset Colombo(DateOnly date, int hour, int minute, int second = 0) =>
        new(date.ToDateTime(new TimeOnly(hour, minute, second)), ColomboTime.Offset);

    private static DateTimeOffset At(int hour, int minute, int second = 0) => Colombo(Wednesday, hour, minute, second);

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

    private static ScheduledJourney Journey(params PatternStop[] stops)
    {
        var pattern = new ServicePattern
        {
            Id = Guid.NewGuid(),
            TrainNumber = "1",
            SourceKind = SourceKind.FictionalDemo,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Stops = [.. stops],
        };
        return new ScheduledJourney { Id = Guid.NewGuid(), PatternId = pattern.Id, ServiceDate = Wednesday, Pattern = pattern };
    }

    private static RunningObservation Seen(
        PatternStop from, PatternStop to, int progress, DateTimeOffset observedAt, int reportedDelay = 0) =>
        new(from.Id, to.Id, progress, reportedDelay, observedAt);

    private static StopRunning Row(RunningState state, PatternStop stop) =>
        state.Stops.Single(r => r.StopId == stop.Id);

    // Gampaha 08:00 -> Ragama 08:10/08:11 -> Fort 08:40
    private readonly PatternStop gampaha = Stop(GampahaStation, 1, null, "08:00", platform: "2");
    private readonly PatternStop ragama = Stop(RagamaStation, 2, "08:10", "08:11", platform: "1");
    private readonly PatternStop fort = Stop(FortStation, 3, "08:40", null, platform: "4");

    private ScheduledJourney Standard() => Journey(gampaha, ragama, fort);

    private static void AssertNoEstimates(RunningState state)
    {
        Assert.All(state.Stops, r =>
        {
            Assert.Null(r.EstimatedArrival);
            Assert.Null(r.EstimatedDeparture);
        });
    }

    [Fact]
    public void No_evidence_is_unknown_and_keeps_the_schedule_and_platforms()
    {
        var state = RunningCalculator.Calculate(Standard(), null, At(8, 5));

        Assert.Equal(Freshness.Unknown, state.Freshness);
        Assert.Null(state.LastObservation);
        Assert.Null(state.CurrentPosition);
        Assert.Null(state.DelaySeconds);
        AssertNoEstimates(state);

        Assert.Equal([gampaha.Id, ragama.Id, fort.Id], state.Stops.Select(r => r.StopId));
        Assert.Equal(At(8, 10), Row(state, ragama).ScheduledArrival);
        Assert.Equal(At(8, 11), Row(state, ragama).ScheduledDeparture);
        Assert.Equal(["2", "1", "4"], state.Stops.Select(r => r.PlannedPlatform));
    }

    [Theory]
    [InlineData(179, Freshness.Current, true)]
    [InlineData(180, Freshness.Ageing, true)]
    [InlineData(239, Freshness.Ageing, true)]
    [InlineData(240, Freshness.Unknown, false)]
    public void Freshness_changes_exactly_at_180_and_240_seconds(int ageSeconds, Freshness expected, bool usable)
    {
        var observedAt = At(8, 5);
        var observation = Seen(gampaha, ragama, 500, observedAt, reportedDelay: 60);

        var state = RunningCalculator.Calculate(Standard(), observation, observedAt.AddSeconds(ageSeconds));

        Assert.Equal(expected, state.Freshness);
        Assert.Equal(observation, state.LastObservation);
        Assert.Equal(usable, state.CurrentPosition is not null);
        Assert.Equal(usable, state.DelaySeconds is not null);
        Assert.Equal(usable, Row(state, fort).EstimatedArrival is not null);
    }

    [Fact]
    public void Ageing_evidence_still_gives_position_delay_and_estimates()
    {
        var observedAt = At(8, 5);
        var observation = Seen(gampaha, ragama, 500, observedAt, reportedDelay: 60);

        var state = RunningCalculator.Calculate(Standard(), observation, observedAt.AddSeconds(200));

        Assert.Equal(Freshness.Ageing, state.Freshness);
        Assert.Equal(new CurrentPosition(gampaha.Id, ragama.Id, 500), state.CurrentPosition);
        Assert.Equal(60, state.DelaySeconds);
        Assert.Equal(At(8, 11), Row(state, ragama).EstimatedArrival);
        Assert.Equal(At(8, 12), Row(state, ragama).EstimatedDeparture);
        Assert.Equal(At(8, 41), Row(state, fort).EstimatedArrival);
    }

    [Fact]
    public void Unknown_evidence_drops_position_and_estimates_but_keeps_history_and_schedule()
    {
        var observedAt = At(8, 5);
        var observation = Seen(gampaha, ragama, 500, observedAt, reportedDelay: 60);

        var state = RunningCalculator.Calculate(Standard(), observation, observedAt.AddSeconds(240));

        Assert.Equal(Freshness.Unknown, state.Freshness);
        Assert.Equal(observation, state.LastObservation);
        Assert.Null(state.CurrentPosition);
        Assert.Null(state.DelaySeconds);
        AssertNoEstimates(state);
        Assert.Equal(At(8, 40), Row(state, fort).ScheduledArrival);
    }

    [Fact]
    public void Overdue_next_stop_clamps_its_estimate_to_now_and_carries_the_delay_forward()
    {
        // Ragama was due at 08:10 but the train is still reported mid-segment at 08:11.
        var observedAt = At(8, 11);
        var now = At(8, 12, 30);
        var observation = Seen(gampaha, ragama, 500, observedAt, reportedDelay: 0);

        var state = RunningCalculator.Calculate(Standard(), observation, now);

        Assert.Equal(Freshness.Current, state.Freshness);
        Assert.Equal(150, state.DelaySeconds);
        Assert.Equal(now, Row(state, ragama).EstimatedArrival);
        Assert.Equal(At(8, 13, 30), Row(state, ragama).EstimatedDeparture);
        Assert.Equal(At(8, 42, 30), Row(state, fort).EstimatedArrival);
    }

    [Theory]
    [InlineData(120, null, 120)]
    [InlineData(120, 300, 300)]
    [InlineData(400, 300, 400)]
    [InlineData(0, -30, 0)]
    public void Delay_is_the_greatest_of_reported_and_confirmed(int reported, int? confirmed, int expected)
    {
        var observedAt = At(8, 5);
        var observation = Seen(gampaha, ragama, 500, observedAt, reportedDelay: reported);

        var state = RunningCalculator.Calculate(Standard(), observation, observedAt.AddSeconds(30), confirmed);

        Assert.Equal(expected, state.DelaySeconds);
    }

    [Fact]
    public void Delay_is_never_negative_so_nothing_is_estimated_early()
    {
        var observedAt = At(8, 5);
        var observation = Seen(gampaha, ragama, 500, observedAt, reportedDelay: -90);

        var state = RunningCalculator.Calculate(Standard(), observation, observedAt.AddSeconds(30));

        Assert.Equal(0, state.DelaySeconds);
        Assert.Equal(At(8, 10), Row(state, ragama).EstimatedArrival);
        Assert.Equal(At(8, 11), Row(state, ragama).EstimatedDeparture);
        Assert.All(state.Stops.Where(r => r.EstimatedDeparture is not null),
            r => Assert.True(r.EstimatedDeparture >= r.ScheduledDeparture));
    }

    [Fact]
    public void Passed_stops_have_no_estimates_while_the_next_ones_do()
    {
        var observedAt = At(8, 30);

        var state = RunningCalculator.Calculate(
            Standard(), Seen(ragama, fort, 400, observedAt, reportedDelay: 120), observedAt);

        Assert.Null(Row(state, gampaha).EstimatedArrival);
        Assert.Null(Row(state, gampaha).EstimatedDeparture);
        Assert.Null(Row(state, ragama).EstimatedArrival);
        Assert.Null(Row(state, ragama).EstimatedDeparture);
        Assert.Equal(At(8, 42), Row(state, fort).EstimatedArrival);
    }

    [Fact]
    public void At_a_stop_it_is_reached_but_has_still_to_depart()
    {
        var observedAt = At(8, 10, 30);

        var state = RunningCalculator.Calculate(
            Standard(), Seen(gampaha, ragama, 1000, observedAt, reportedDelay: 60), observedAt);

        Assert.Equal(new CurrentPosition(gampaha.Id, ragama.Id, 1000), state.CurrentPosition);
        Assert.Null(Row(state, gampaha).EstimatedDeparture);
        Assert.Null(Row(state, ragama).EstimatedArrival);
        Assert.Equal(At(8, 12), Row(state, ragama).EstimatedDeparture);
        Assert.Equal(At(8, 41), Row(state, fort).EstimatedArrival);
    }

    [Fact]
    public void Arriving_at_the_final_stop_leaves_nothing_to_estimate()
    {
        var observedAt = At(8, 40, 30);

        var state = RunningCalculator.Calculate(
            Standard(), Seen(ragama, fort, 1000, observedAt, reportedDelay: 90), observedAt.AddSeconds(30));

        Assert.Equal(Freshness.Current, state.Freshness);
        Assert.Equal(90, state.DelaySeconds);
        AssertNoEstimates(state);
    }

    [Fact]
    public void A_repeated_station_is_told_apart_by_stop_occurrence()
    {
        // Gampaha 08:00, Fort 08:40/08:45, Gampaha 09:30/09:35, Ragama 09:45.
        var first = Stop(GampahaStation, 1, null, "08:00");
        var fortStop = Stop(FortStation, 2, "08:40", "08:45");
        var second = Stop(GampahaStation, 3, "09:30", "09:35");
        var last = Stop(RagamaStation, 4, "09:45", null);
        var journey = Journey(first, fortStop, second, last);

        var outbound = At(8, 20);
        var heading = RunningCalculator.Calculate(
            journey, Seen(first, fortStop, 300, outbound, reportedDelay: 60), outbound);
        var returning = At(9, 10);
        var back = RunningCalculator.Calculate(
            journey, Seen(fortStop, second, 400, returning, reportedDelay: 120), returning);

        // Heading for Fort: the first Gampaha is behind, the second one is still ahead.
        Assert.Null(Row(heading, first).EstimatedDeparture);
        Assert.Equal(At(8, 41), Row(heading, fortStop).EstimatedArrival);
        Assert.Equal(At(9, 31), Row(heading, second).EstimatedArrival);

        // Heading for the second Gampaha: Fort is behind and only the later occurrence is next.
        Assert.Null(Row(back, first).EstimatedDeparture);
        Assert.Null(Row(back, fortStop).EstimatedDeparture);
        Assert.Equal(At(9, 32), Row(back, second).EstimatedArrival);
        Assert.Equal(At(9, 47), Row(back, last).EstimatedArrival);
    }

    [Fact]
    public void Adjacent_stops_are_neighbours_in_order_not_consecutive_sequence_numbers()
    {
        var a = Stop(GampahaStation, 10, null, "08:00");
        var b = Stop(RagamaStation, 20, "08:10", "08:11");
        var c = Stop(FortStation, 30, "08:40", null);
        var journey = Journey(c, a, b);
        var observedAt = At(8, 5);

        var state = RunningCalculator.Calculate(journey, Seen(a, b, 500, observedAt, reportedDelay: 60), observedAt);

        Assert.Equal(Freshness.Current, state.Freshness);
        Assert.Equal([a.Id, b.Id, c.Id], state.Stops.Select(r => r.StopId));
        Assert.Equal(At(8, 41), Row(state, c).EstimatedArrival);
    }

    [Fact]
    public void Evidence_that_does_not_fit_the_journey_is_unknown()
    {
        var observedAt = At(8, 5);
        var elsewhere = Stop(FortStation, 9, "09:00", null);

        var skipped = Seen(gampaha, fort, 500, observedAt);
        var reversed = Seen(ragama, gampaha, 500, observedAt);
        var foreign = Seen(gampaha, elsewhere, 500, observedAt);
        var overshoot = Seen(gampaha, ragama, 1001, observedAt);
        var negative = Seen(gampaha, ragama, -1, observedAt);

        foreach (var observation in new[] { skipped, reversed, foreign, overshoot, negative })
        {
            var state = RunningCalculator.Calculate(Standard(), observation, observedAt);

            Assert.Equal(Freshness.Unknown, state.Freshness);
            Assert.Null(state.CurrentPosition);
            Assert.Null(state.DelaySeconds);
            AssertNoEstimates(state);
        }
    }

    // Gampaha 23:30 -> Ragama 23:58/00:03 (day offset 1) -> Fort 00:30 (day offset 1).
    private (ScheduledJourney Journey, PatternStop From, PatternStop To, PatternStop Last) Overnight()
    {
        var from = Stop(GampahaStation, 1, null, "23:30");
        var to = Stop(RagamaStation, 2, "23:58", "00:03", dayOffset: 1);
        var last = Stop(FortStation, 3, "00:30", null, dayOffset: 1);
        return (Journey(from, to, last), from, to, last);
    }

    [Fact]
    public void Dwell_across_midnight_keeps_arrival_on_the_service_date_and_departure_after_it()
    {
        var (journey, from, to, last) = Overnight();
        var observedAt = At(23, 50);

        var state = RunningCalculator.Calculate(journey, Seen(from, to, 600, observedAt, reportedDelay: 120), observedAt);

        Assert.Equal(At(23, 58), Row(state, to).ScheduledArrival);
        Assert.Equal(Colombo(Thursday, 0, 3), Row(state, to).ScheduledDeparture);
        Assert.Equal(Colombo(Thursday, 0, 0), Row(state, to).EstimatedArrival);
        Assert.Equal(Colombo(Thursday, 0, 5), Row(state, to).EstimatedDeparture);
        Assert.Equal(Colombo(Thursday, 0, 32), Row(state, last).EstimatedArrival);
    }

    [Fact]
    public void Overdue_arrival_before_midnight_is_measured_against_the_date_it_was_due()
    {
        var (journey, from, to, last) = Overnight();
        var observedAt = Colombo(Thursday, 0, 4);

        // Due at 23:58 on Wednesday, so at 00:05 Thursday it is 7 minutes overdue.
        var state = RunningCalculator.Calculate(
            journey, Seen(from, to, 900, observedAt), Colombo(Thursday, 0, 5));

        Assert.Equal(420, state.DelaySeconds);
        Assert.Equal(Colombo(Thursday, 0, 5), Row(state, to).EstimatedArrival);
        Assert.Equal(Colombo(Thursday, 0, 10), Row(state, to).EstimatedDeparture);
        Assert.Equal(Colombo(Thursday, 0, 37), Row(state, last).EstimatedArrival);
    }
}
