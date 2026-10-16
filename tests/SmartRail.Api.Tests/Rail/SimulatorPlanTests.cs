using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Tests.Rail;

// Where a simulated train is at a given moment, from the timetable alone. No database or clock.
public class SimulatorPlanTests
{
    private static readonly DateOnly Wednesday = new(2026, 10, 14);
    private static readonly DateOnly Thursday = new(2026, 10, 15);

    private static DateTimeOffset At(int hour, int minute, int second = 0, DateOnly? date = null) =>
        new((date ?? Wednesday).ToDateTime(new TimeOnly(hour, minute, second)), ColomboTime.Offset);

    private static PatternStop Stop(int sequence, string? arrival, string? departure, int dayOffset = 0) =>
        new()
        {
            Id = Guid.NewGuid(),
            StationId = Guid.NewGuid(),
            Sequence = sequence,
            ArrivalLocal = arrival is null ? null : TimeOnly.Parse(arrival),
            DepartureLocal = departure is null ? null : TimeOnly.Parse(departure),
            DayOffset = dayOffset,
        };

    private static ScheduledJourney Journey(params PatternStop[] stops)
    {
        var pattern = new ServicePattern
        {
            Id = Guid.NewGuid(),
            TrainNumber = "SIM",
            SourceKind = SourceKind.FictionalDemo,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Stops = [.. stops],
        };
        return new ScheduledJourney { Id = Guid.NewGuid(), PatternId = pattern.Id, ServiceDate = Wednesday, Pattern = pattern };
    }

    // Gampaha 07:20 -> Ragama 07:35/07:36 -> Fort 08:15 (like the seeded SIM-1003).
    private readonly PatternStop gampaha = Stop(1, null, "07:20");
    private readonly PatternStop ragama = Stop(2, "07:35", "07:36");
    private readonly PatternStop fort = Stop(3, "08:15", null);

    private ScheduledJourney Standard() => Journey(gampaha, ragama, fort);

    [Fact]
    public void Moments_are_snapped_down_to_a_30_second_boundary()
    {
        Assert.Equal(At(7, 20, 0), SimulatorPlan.TickTime(At(7, 20, 29)));
        Assert.Equal(At(7, 20, 0), SimulatorPlan.TickTime(At(7, 20, 0)));
        Assert.Equal(At(7, 20, 30), SimulatorPlan.TickTime(At(7, 20, 30)));
        Assert.Equal(At(7, 20, 30), SimulatorPlan.TickTime(At(7, 20, 59).AddMilliseconds(900)));
    }

    [Fact]
    public void A_window_has_one_event_id_and_each_journey_and_window_has_its_own()
    {
        var journey = Guid.NewGuid();
        var tick = SimulatorPlan.TickTime(At(7, 20, 10));

        Assert.Equal(SimulatorPlan.EventId(journey, tick), SimulatorPlan.EventId(journey, SimulatorPlan.TickTime(At(7, 20, 29))));
        Assert.NotEqual(SimulatorPlan.EventId(journey, tick), SimulatorPlan.EventId(journey, tick.AddSeconds(30)));
        Assert.NotEqual(SimulatorPlan.EventId(journey, tick), SimulatorPlan.EventId(Guid.NewGuid(), tick));
        Assert.True(SimulatorPlan.EventId(journey, tick).Length <= ObservationRules.MaxEventIdLength);
    }

    [Fact]
    public void Nothing_is_reported_before_the_first_departure()
    {
        Assert.Null(SimulatorPlan.ReportAt(Standard(), At(7, 19, 30), 0));
        Assert.Null(SimulatorPlan.ReportAt(Standard(), At(6, 0), 0));
    }

    [Fact]
    public void Reporting_starts_at_the_first_departure_on_the_first_segment()
    {
        var report = SimulatorPlan.ReportAt(Standard(), At(7, 20), 0)!;

        Assert.Equal(gampaha.Id, report.FromStopId);
        Assert.Equal(ragama.Id, report.ToStopId);
        Assert.Equal(0, report.ProgressPermille);
        Assert.Equal(At(7, 20), report.ObservedAt);
    }

    [Theory]
    [InlineData(7, 27, 30, 500)]    // half way through the 15 minutes
    [InlineData(7, 24, 0, 266)]     // 4 of 15 minutes
    [InlineData(7, 34, 0, 933)]     // 14 of 15 minutes
    [InlineData(7, 34, 30, 966)]    // 14.5 of 15 minutes
    public void Progress_follows_the_timetable_between_stops(int hour, int minute, int second, int expected)
    {
        var report = SimulatorPlan.ReportAt(Standard(), At(hour, minute, second), 0)!;

        Assert.Equal(gampaha.Id, report.FromStopId);
        Assert.Equal(ragama.Id, report.ToStopId);
        Assert.Equal(expected, report.ProgressPermille);
    }

    [Fact]
    public void A_moving_train_never_reports_1000_just_before_it_arrives()
    {
        var report = SimulatorPlan.ReportAt(Standard(), At(7, 34, 59).AddMilliseconds(999), 0)!;

        Assert.Equal(ragama.Id, report.ToStopId);
        Assert.Equal(999, report.ProgressPermille);
    }

    [Fact]
    public void During_the_dwell_the_train_holds_at_1000_on_the_inbound_segment_until_it_departs()
    {
        foreach (var report in new[] { At(7, 35, 0), At(7, 35, 30) }.Select(t => SimulatorPlan.ReportAt(Standard(), t, 0)!))
        {
            Assert.Equal(gampaha.Id, report.FromStopId);
            Assert.Equal(ragama.Id, report.ToStopId);
            Assert.Equal(1000, report.ProgressPermille);
        }

        // At 07:36:00 it leaves: the outbound segment starts at 0.
        var departed = SimulatorPlan.ReportAt(Standard(), At(7, 36, 0), 0)!;
        Assert.Equal(ragama.Id, departed.FromStopId);
        Assert.Equal(fort.Id, departed.ToStopId);
        Assert.Equal(0, departed.ProgressPermille);
    }

    [Fact]
    public void The_final_arrival_is_reported_once_and_nothing_follows()
    {
        var journey = Standard();

        var arrival = SimulatorPlan.ReportAt(journey, At(8, 15, 0), 0)!;
        Assert.Equal(ragama.Id, arrival.FromStopId);
        Assert.Equal(fort.Id, arrival.ToStopId);
        Assert.Equal(1000, arrival.ProgressPermille);

        Assert.Null(SimulatorPlan.ReportAt(journey, At(8, 15, 30), 0));
        Assert.Null(SimulatorPlan.ReportAt(journey, At(9, 0, 0), 0));
    }

    [Fact]
    public void An_arrival_between_two_ticks_is_still_reported_exactly_once()
    {
        var journey = Journey(gampaha, Stop(2, "08:15:10", null));

        var reports = Enumerable.Range(0, 4)
            .Select(i => SimulatorPlan.ReportAt(journey, At(8, 14, 30).AddSeconds(30 * i), 0))
            .ToList();

        // 08:14:30 moving, 08:15:00 moving, 08:15:30 arrived, 08:16:00 finished.
        Assert.InRange(reports[1]!.ProgressPermille, 900, 999);
        Assert.Equal(1000, reports[2]!.ProgressPermille);
        Assert.Null(reports[3]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(300)]
    public void A_demo_delay_never_lets_a_train_leave_early_or_move_backwards_and_arrives_once(int delay)
    {
        var journey = Standard();
        var firstDeparture = At(7, 20).AddSeconds(delay);
        var reports = new List<SimulatedReport>();

        for (var tick = At(6, 0); tick < At(10, 0); tick = tick.AddSeconds(30))
        {
            var report = SimulatorPlan.ReportAt(journey, tick, delay);
            if (report is null)
            {
                if (reports.Count == 0)
                {
                    Assert.True(tick < firstDeparture, $"no report at {tick:HH:mm:ss} although the train had departed");
                }

                continue;
            }

            Assert.True(tick >= firstDeparture, $"reported {tick:HH:mm:ss} before the delayed departure");
            Assert.Equal(delay, report.DelaySeconds);
            reports.Add(report);
        }

        Assert.NotEmpty(reports);
        var order = reports.Select(r => (journey.Pattern.Stops.First(s => s.Id == r.ToStopId).Sequence * 1000) + r.ProgressPermille).ToList();
        Assert.Equal(order.Order().ToList(), order);
        Assert.Equal(1, reports.Count(r => r.ToStopId == fort.Id && r.ProgressPermille == 1000));
        Assert.Equal(1000, reports[^1].ProgressPermille);
        Assert.Equal(fort.Id, reports[^1].ToStopId);
    }

    [Fact]
    public void A_delay_shifts_the_whole_run_later_by_that_much()
    {
        Assert.Null(SimulatorPlan.ReportAt(Standard(), At(7, 21, 30), 120));
        var first = SimulatorPlan.ReportAt(Standard(), At(7, 22, 0), 120)!;
        Assert.Equal(0, first.ProgressPermille);
        Assert.Equal(120, first.DelaySeconds);
    }

    [Fact]
    public void A_dwell_that_crosses_midnight_holds_and_then_departs_on_the_next_day()
    {
        // Fort 23:45, Ragama arrives 00:25 and leaves 00:26 (day offset 1), Gampaha 00:45.
        var from = Stop(1, null, "23:45");
        var via = Stop(2, "00:25", "00:26", dayOffset: 1);
        var to = Stop(3, "00:45", null, dayOffset: 1);
        var journey = Journey(from, via, to);

        var holding = SimulatorPlan.ReportAt(journey, At(0, 25, 30, Thursday), 0)!;
        Assert.Equal(from.Id, holding.FromStopId);
        Assert.Equal(via.Id, holding.ToStopId);
        Assert.Equal(1000, holding.ProgressPermille);

        var departed = SimulatorPlan.ReportAt(journey, At(0, 26, 0, Thursday), 0)!;
        Assert.Equal(via.Id, departed.FromStopId);
        Assert.Equal(to.Id, departed.ToStopId);
        Assert.Equal(0, departed.ProgressPermille);
    }

    [Fact]
    public void A_journey_with_fewer_than_two_stops_reports_nothing()
    {
        Assert.Null(SimulatorPlan.ReportAt(Journey(Stop(1, null, "07:20")), At(7, 20), 0));
    }
}

// How the simulator is switched on, with no host started.
public class SimulatorSetupTests
{
    private static WebApplicationBuilder Builder(string? enabled, string? delay = null)
    {
        var builder = WebApplication.CreateBuilder();
        if (enabled is not null)
        {
            builder.Configuration["Simulator:Enabled"] = enabled;
        }

        if (delay is not null)
        {
            builder.Configuration["Simulator:DemoDelaySeconds"] = delay;
        }

        return builder;
    }

    private static bool HasWorker(WebApplicationBuilder builder) =>
        builder.Services.Any(d => d.ImplementationType == typeof(SimulatorWorker));

    [Fact]
    public void The_worker_is_off_unless_the_simulator_is_enabled_and_there_is_a_database()
    {
        var off = Builder(null);
        off.AddSmartRailSimulator(hasDatabase: true);
        var disabled = Builder("false");
        disabled.AddSmartRailSimulator(hasDatabase: true);
        var noDatabase = Builder("true");
        noDatabase.AddSmartRailSimulator(hasDatabase: false);
        var on = Builder("true");
        on.AddSmartRailSimulator(hasDatabase: true);

        Assert.False(HasWorker(off));
        Assert.False(HasWorker(disabled));
        Assert.False(HasWorker(noDatabase));
        Assert.True(HasWorker(on));
    }

    [Fact]
    public void A_negative_demo_delay_stops_the_API_from_starting()
    {
        var builder = Builder("true", "-1");

        var error = Assert.Throws<InvalidOperationException>(() => builder.AddSmartRailSimulator(hasDatabase: true));
        Assert.Contains("Simulator:DemoDelaySeconds", error.Message);
    }
}
