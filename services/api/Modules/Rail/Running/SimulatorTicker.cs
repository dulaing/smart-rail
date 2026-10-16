using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Rail.Running;

// Which journeys the demo operator has paused. Lives in memory, so a restart resumes everything.
public sealed class SimulatorControl
{
    private readonly ConcurrentDictionary<Guid, byte> _paused = new();

    public bool IsPaused(Guid journeyId) => _paused.ContainsKey(journeyId);

    public void Pause(Guid journeyId) => _paused[journeyId] = 0;

    public void Resume(Guid journeyId) => _paused.TryRemove(journeyId, out _);
}

public sealed record SimulatorOptions(bool Enabled, int DemoDelaySeconds);

// What one tick did, for the log and the tests.
public sealed record TickSummary(int Created, int Repeated, int Paused, int Rejected);

// One 30-second step of the simulator: report where every fictional train in service is now,
// by sending each report through the same checks as a real feed would pass.
public sealed class SimulatorTicker(
    SimulatorControl control, SimulatorOptions options, TimeProvider time, ILogger<SimulatorTicker> logger)
{
    // An overnight train that left up to two service dates ago can still be running.
    private const int ServiceDatesBack = 2;
    private const int ServiceDatesAhead = 1;

    public async Task<TickSummary> TickAsync(SmartRailDbContext db, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var tickTime = SimulatorPlan.TickTime(now);
        var today = ColomboTime.ServiceDateOf(now);
        var from = today.AddDays(-ServiceDatesBack);
        var to = today.AddDays(ServiceDatesAhead);

        // Only the fictional, seeded journeys are ever simulated.
        var journeys = await db.Journeys
            .AsNoTracking()
            .Include(j => j.Pattern).ThenInclude(p => p.Stops)
            .Where(j => j.DemoSeedKey != null && j.ServiceDate >= from && j.ServiceDate <= to
                && j.Pattern.SourceKind == SourceKind.FictionalDemo)
            .ToListAsync(cancellationToken);

        int created = 0, repeated = 0, paused = 0, rejected = 0;
        foreach (var journey in journeys.Where(j => j.Pattern.RunsOn(j.ServiceDate)).OrderBy(j => j.Id))
        {
            if (SimulatorPlan.ReportAt(journey, tickTime, options.DemoDelaySeconds) is not { } report)
            {
                continue;
            }

            if (control.IsPaused(journey.Id))
            {
                paused++;
                continue;
            }

            var request = new ObservationRequest(
                SimulatorPlan.EventId(journey.Id, tickTime),
                journey.Id,
                report.FromStopId,
                report.ToStopId,
                report.ProgressPermille,
                report.DelaySeconds,
                report.ObservedAt.ToString("O"));
            var result = await ObservationIngestion.IngestAsync(db, time, request, cancellationToken);

            switch (result.Status)
            {
                case IngestStatus.Created:
                    created++;
                    break;
                case IngestStatus.Duplicate:
                    repeated++;
                    break;
                default:
                    rejected++;
                    logger.LogWarning("Simulator report for journey {JourneyId} was {Status}: {Detail}",
                        journey.Id, result.Status, result.Detail);
                    break;
            }
        }

        return new TickSummary(created, repeated, paused, rejected);
    }
}
