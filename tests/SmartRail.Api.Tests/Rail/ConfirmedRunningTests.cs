using SmartRail.Api.Modules.Rail;
using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Tests.Rail;

// Staff decisions applied to the running calculation, with fixed instants and no database.
public class ConfirmedRunningTests
{
    private static readonly DateOnly Wednesday = new(2026, 10, 14);

    private static DateTimeOffset At(int hour, int minute, int second = 0) =>
        new(Wednesday.ToDateTime(new TimeOnly(hour, minute, second)), ColomboTime.Offset);

    private static PatternStop Stop(int sequence, string? arrival, string? departure, string platform) =>
        new()
        {
            Id = Guid.NewGuid(),
            StationId = Guid.NewGuid(),
            Sequence = sequence,
            ArrivalLocal = arrival is null ? null : TimeOnly.Parse(arrival),
            DepartureLocal = departure is null ? null : TimeOnly.Parse(departure),
            PlannedPlatform = platform,
        };

    // A 08:00 -> B 08:10/08:11 -> C 08:25/08:26 -> D 08:40
    private readonly PatternStop a = Stop(1, null, "08:00", "1");
    private readonly PatternStop b = Stop(2, "08:10", "08:11", "2");
    private readonly PatternStop c = Stop(3, "08:25", "08:26", "3");
    private readonly PatternStop d = Stop(4, "08:40", null, "4");

    private ScheduledJourney Journey()
    {
        var pattern = new ServicePattern
        {
            Id = Guid.NewGuid(),
            TrainNumber = "1",
            SourceKind = SourceKind.FictionalDemo,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Stops = [a, b, c, d],
        };
        return new ScheduledJourney { Id = Guid.NewGuid(), PatternId = pattern.Id, ServiceDate = Wednesday, Pattern = pattern };
    }

    // Between A and B, halfway, seen at 08:05.
    private RunningObservation Seen(int reportedDelay = 0) => new(a.Id, b.Id, 500, reportedDelay, At(8, 5));

    private static readonly DateTimeOffset Fresh = At(8, 5, 30);
    private static readonly DateTimeOffset Stale = At(8, 5).AddSeconds(240);

    private static StopRunning Row(RunningState state, PatternStop stop) => state.Stops.Single(r => r.StopId == stop.Id);

    private static ConfirmedOverrides Overrides(
        int? delay = null, bool cancelled = false, Guid? finalStop = null, params (Guid Stop, string Platform, DateTimeOffset At)[] platforms) =>
        new(delay, cancelled, finalStop, platforms.ToDictionary(p => p.Stop, p => new ConfirmedPlatform(p.Platform, p.At)));

    [Fact]
    public void No_decisions_changes_nothing()
    {
        var withNone = RunningCalculator.Calculate(Journey(), Seen(), Fresh, overrides: ConfirmedOverrides.None);
        var without = RunningCalculator.Calculate(Journey(), Seen(), Fresh);

        Assert.Equal(Availabilities.AsScheduled, withNone.Availability);
        Assert.Null(withNone.ConfirmedDelayMinutes);
        Assert.Null(withNone.FinalStopId);
        Assert.All(withNone.Stops, r =>
        {
            Assert.True(r.Served);
            Assert.Null(r.ConfirmedPlatform);
        });
        Assert.Equal(without.Stops.Select(r => r.EstimatedArrival), withNone.Stops.Select(r => r.EstimatedArrival));
    }

    [Fact]
    public void A_confirmed_delay_moves_the_estimates_and_leaves_the_schedule_alone()
    {
        var journey = Journey();

        var state = RunningCalculator.Calculate(journey, Seen(), Fresh, overrides: Overrides(delay: 10));

        Assert.Equal(600, state.DelaySeconds);
        Assert.Equal(10, state.ConfirmedDelayMinutes);
        Assert.Equal(At(8, 20), Row(state, b).EstimatedArrival);
        Assert.Equal(At(8, 10), Row(state, b).ScheduledArrival);
        Assert.Equal(At(8, 50), Row(state, d).EstimatedArrival);
        Assert.Equal(At(8, 40), Row(state, d).ScheduledArrival);
    }

    [Fact]
    public void The_larger_of_a_reported_and_a_confirmed_delay_wins()
    {
        Assert.Equal(900, RunningCalculator.Calculate(Journey(), Seen(reportedDelay: 900), Fresh, overrides: Overrides(delay: 10)).DelaySeconds);
        Assert.Equal(600, RunningCalculator.Calculate(Journey(), Seen(reportedDelay: 60), Fresh, overrides: Overrides(delay: 10)).DelaySeconds);
    }

    [Fact]
    public void A_confirmed_delay_does_not_make_stale_evidence_usable()
    {
        var state = RunningCalculator.Calculate(Journey(), Seen(), Stale, overrides: Overrides(delay: 10));

        Assert.Equal(Freshness.Unknown, state.Freshness);
        Assert.Null(state.DelaySeconds);
        Assert.Equal(10, state.ConfirmedDelayMinutes);
        Assert.All(state.Stops, r =>
        {
            Assert.Null(r.EstimatedArrival);
            Assert.Null(r.EstimatedDeparture);
            Assert.NotNull(r.ScheduledDeparture ?? r.ScheduledArrival);
        });
    }

    [Fact]
    public void A_cancellation_removes_every_estimate_and_serves_no_stop_but_keeps_the_evidence()
    {
        var state = RunningCalculator.Calculate(Journey(), Seen(), Fresh, overrides: Overrides(delay: 10, cancelled: true));

        Assert.Equal(Availabilities.Cancelled, state.Availability);
        Assert.Equal(Freshness.Current, state.Freshness);
        Assert.NotNull(state.LastObservation);
        Assert.Null(state.CurrentPosition);
        Assert.Null(state.DelaySeconds);
        Assert.All(state.Stops, r =>
        {
            Assert.False(r.Served);
            Assert.Null(r.EstimatedArrival);
            Assert.Null(r.EstimatedDeparture);
        });
        Assert.Equal(At(8, 10), Row(state, b).ScheduledArrival);
    }

    [Fact]
    public void An_early_end_keeps_the_final_stop_with_its_arrival_and_drops_later_stops()
    {
        var state = RunningCalculator.Calculate(Journey(), Seen(), Fresh, overrides: Overrides(finalStop: c.Id));

        Assert.Equal(Availabilities.EndsEarly, state.Availability);
        Assert.Equal(c.Id, state.FinalStopId);
        Assert.True(Row(state, a).Served);
        Assert.True(Row(state, b).Served);
        Assert.True(Row(state, c).Served);
        Assert.False(Row(state, d).Served);

        // The new final stop still has its arrival; it has no departure to wait for.
        Assert.Equal(At(8, 25), Row(state, c).EstimatedArrival);
        Assert.Null(Row(state, d).EstimatedArrival);
        Assert.Null(Row(state, d).EstimatedDeparture);
        Assert.Equal(At(8, 40), Row(state, d).ScheduledArrival);
        Assert.Equal(Freshness.Current, state.Freshness);
    }

    [Fact]
    public void Cancellation_outranks_an_early_end()
    {
        var state = RunningCalculator.Calculate(Journey(), Seen(), Fresh, overrides: Overrides(cancelled: true, finalStop: c.Id));

        Assert.Equal(Availabilities.Cancelled, state.Availability);
        Assert.All(state.Stops, r => Assert.False(r.Served));
    }

    [Fact]
    public void A_final_stop_that_is_not_on_the_journey_is_ignored()
    {
        var state = RunningCalculator.Calculate(Journey(), Seen(), Fresh, overrides: Overrides(finalStop: Guid.NewGuid()));

        Assert.Equal(Availabilities.AsScheduled, state.Availability);
        Assert.Null(state.FinalStopId);
        Assert.All(state.Stops, r => Assert.True(r.Served));
    }

    [Fact]
    public void A_confirmed_platform_and_its_time_survive_unknown_evidence()
    {
        var confirmedAt = At(7, 58);
        var overrides = Overrides(platforms: (b.Id, "5", confirmedAt));

        foreach (var now in new[] { Fresh, Stale })
        {
            var state = RunningCalculator.Calculate(Journey(), Seen(), now, overrides: overrides);

            Assert.Equal(("5", confirmedAt, "5", "2"),
                (Row(state, b).ConfirmedPlatform, Row(state, b).ConfirmedPlatformAt, Row(state, b).Platform, Row(state, b).PlannedPlatform));
            Assert.Null(Row(state, c).ConfirmedPlatform);
            Assert.Equal("3", Row(state, c).Platform);
        }
    }

    [Fact]
    public void Withdrawing_an_early_end_restores_estimates_only_while_the_evidence_is_fresh()
    {
        var journey = Journey();
        var ended = Overrides(finalStop: c.Id);

        // Fresh: the later stop comes back with a prediction once the ending is withdrawn.
        Assert.Null(Row(RunningCalculator.Calculate(journey, Seen(), Fresh, overrides: ended), d).EstimatedArrival);
        Assert.Equal(At(8, 40), Row(RunningCalculator.Calculate(journey, Seen(), Fresh, overrides: ConfirmedOverrides.None), d).EstimatedArrival);

        // Stale: the stop is served again, but nothing is predicted for it.
        var restored = RunningCalculator.Calculate(journey, Seen(), Stale, overrides: ConfirmedOverrides.None);
        Assert.True(Row(restored, d).Served);
        Assert.Null(Row(restored, d).EstimatedArrival);
        Assert.Equal(At(8, 40), Row(restored, d).ScheduledArrival);
    }
}
