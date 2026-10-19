using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Rail;
using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Infrastructure;

// Explicit, one-shot evidence for the Operations workspace. These journeys are excluded from
// the continuous simulator (null DemoSeedKey), so polling never refreshes their observations.
public static class OperationsDemoSeeder
{
    public sealed record SeededJourney(string TrainNumber, Guid JourneyId, DateOnly ServiceDate);

    public static async Task<IReadOnlyList<SeededJourney>> SeedAsync(
        SmartRailDbContext db, TimeProvider time, DateTimeOffset anchor, CancellationToken cancellationToken = default)
    {
        anchor = DateTimeOffset.FromUnixTimeSeconds(anchor.ToUnixTimeSeconds());
        if (anchor > time.GetUtcNow())
        {
            throw new InvalidOperationException("Seed:OperationsAnchor cannot be in the future.");
        }

        var today = ColomboTime.ServiceDateOf(anchor);
        // Reuse the established station IDs/names; do not replace existing schedules or changes.
        await DatabaseSetup.SeedAsync(db, today.AddDays(-1), 2);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var stations = await db.Stations.AsNoTracking().ToDictionaryAsync(s => s.Code, s => s.Id, cancellationToken);
        var result = new List<SeededJourney>();
        foreach (var (suffix, age) in new (string Suffix, int? Age)[]
                 { ("FRESH", 0), ("AGEING", 185), ("UNKNOWN", 300), ("OVERNIGHT", null) })
        {
            var train = $"SIM-OPS-{suffix}";
            var key = $"{train}:{anchor.ToString("O", CultureInfo.InvariantCulture)}";
            var journeyId = StableIds.For("operations-demo-journey", key);
            var patternId = StableIds.For("operations-demo-pattern", key);
            var overnight = suffix == "OVERNIGHT";
            var departure = overnight
                ? ColomboTime.ToInstant(today.AddDays(-1), new TimeOnly(23, 45), 0)
                : anchor.AddMinutes(-10);
            var serviceDate = ColomboTime.ServiceDateOf(departure);
            var instants = overnight
                ? new[] { departure, departure.AddMinutes(40), departure.AddMinutes(60) }
                : new[] { departure, anchor.AddMinutes(10), anchor.AddMinutes(30) };
            var codes = new[] { "FOT", "RGM", "GPH" };
            var stops = instants.Select((instant, i) => new PatternStop
            {
                Id = StableIds.For("operations-demo-stop", $"{key}:{i}"),
                PatternId = patternId,
                StationId = stations[codes[i]],
                Sequence = i + 1,
                ArrivalLocal = i == 0 ? null : TimeOnly.FromDateTime(instant.ToOffset(ColomboTime.Offset).DateTime),
                DepartureLocal = i == 2 ? null : TimeOnly.FromDateTime(instant.ToOffset(ColomboTime.Offset).DateTime),
                DayOffset = ColomboTime.ServiceDateOf(instant).DayNumber - serviceDate.DayNumber,
                PlannedPlatform = i == 0 ? "6" : i == 1 ? "1" : "2",
            }).ToList();

            if (!await db.Journeys.AnyAsync(j => j.Id == journeyId, cancellationToken))
            {
                var pattern = new ServicePattern
                {
                    Id = patternId, TrainNumber = train, SourceKind = SourceKind.FictionalDemo,
                    EffectiveFrom = serviceDate, EffectiveTo = serviceDate, Stops = stops,
                };
                db.Journeys.Add(new ScheduledJourney
                {
                    Id = journeyId, PatternId = patternId, Pattern = pattern, ServiceDate = serviceDate,
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            if (age is { } seconds)
            {
                var observation = await ObservationIngestion.IngestAsync(db, time, new ObservationRequest(
                    $"ops-seed-{journeyId:N}", journeyId, stops[0].Id, stops[1].Id, 400, 120,
                    anchor.AddSeconds(-seconds).ToString("O", CultureInfo.InvariantCulture)), cancellationToken);
                if (observation.Status is not (IngestStatus.Created or IngestStatus.Duplicate))
                {
                    throw new InvalidOperationException($"Could not seed {train}: {observation.Detail}");
                }
            }

            result.Add(new SeededJourney(train, journeyId, serviceDate));
        }

        await OperationsSeeder.SeedVersionsAsync(db, new JourneyStopReader(db), time.GetUtcNow().ToUniversalTime(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
