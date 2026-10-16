using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Rail.Running;

// One position the simulator reports.
public sealed record SimulatedReport(
    Guid FromStopId, Guid ToStopId, int ProgressPermille, int DelaySeconds, DateTimeOffset ObservedAt);

// Where a simulated train is at a moment, worked out from the timetable alone. No clock,
// database or memory: the same journey, moment and delay always give the same answer, which is
// what lets a retry or a restart repeat a report instead of inventing a new one.
public static class SimulatorPlan
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    // Moments are snapped down to a 30-second boundary, so the report for a given window is
    // always the same one however many times, or how late within the window, it is produced.
    public static DateTimeOffset TickTime(DateTimeOffset now)
    {
        var seconds = (long)Interval.TotalSeconds;
        return DateTimeOffset.FromUnixTimeSeconds(Math.DivRem(now.ToUnixTimeSeconds(), seconds, out _) * seconds);
    }

    // The same journey and window always get the same event ID.
    public static string EventId(Guid journeyId, DateTimeOffset tickTime) =>
        $"sim-{journeyId:N}-{tickTime.ToUnixTimeSeconds()}";

    // The simulated train runs the timetable late by a fixed delay, so it never leaves a stop
    // before its scheduled time. It reports nothing before it first departs, and nothing after
    // the window in which it reaches its final stop.
    public static SimulatedReport? ReportAt(ScheduledJourney journey, DateTimeOffset tickTime, int demoDelaySeconds)
    {
        var stops = journey.Pattern.Stops.OrderBy(s => s.Sequence).ToList();
        if (stops.Count < 2)
        {
            return null;
        }

        var times = stops.Select(s => ColomboTime.StopInstants(journey.ServiceDate, s)).ToList();
        DateTimeOffset Arrives(int i) => times[i].Arrival ?? times[i].Departure!.Value;
        DateTimeOffset Departs(int i) => times[i].Departure ?? times[i].Arrival!.Value;

        // Where the timetable says the train would be if it ran on time.
        var scheduled = tickTime - TimeSpan.FromSeconds(demoDelaySeconds);
        var last = stops.Count - 1;

        SimulatedReport Report(int to, int progress) =>
            new(stops[to - 1].Id, stops[to].Id, progress, demoDelaySeconds, tickTime);

        if (scheduled < Departs(0) || scheduled >= Arrives(last) + Interval)
        {
            return null;
        }

        for (var to = 1; to <= last; to++)
        {
            // Between leaving the previous stop and reaching this one.
            if (scheduled >= Departs(to - 1) && scheduled < Arrives(to))
            {
                var travel = Arrives(to) - Departs(to - 1);
                var done = (scheduled - Departs(to - 1)).Ticks;
                return Report(to, Math.Min(999, (int)(1000L * done / travel.Ticks)));
            }

            // Waiting at this stop: hold the arrival (progress 1000 on the inbound segment) until
            // the train leaves, and only then start the next segment.
            if (to < last && scheduled >= Arrives(to) && scheduled < Departs(to))
            {
                return Report(to, RunningCalculator.FullProgress);
            }
        }

        // Reached the final stop. The window is one interval long, so this is reported once.
        return scheduled >= Arrives(last) ? Report(last, RunningCalculator.FullProgress) : null;
    }
}
