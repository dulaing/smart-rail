using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Rail.Timetable;

// Seeds a small fictional Gampaha-Colombo corridor so the public reads have data. Every
// pattern is labelled FICTIONAL_DEMO: none is a verified running service. Running it again
// adds only what is missing, because IDs come from stable keys and journeys carry a
// demo_seed_key.
public static class DemoTimetableSeeder
{
    private sealed record StopSpec(string Station, string? Arrival, string? Departure, int DayOffset = 0, string? Platform = null);

    private sealed record PatternSpec(string TrainNumber, OperatingDays Days, StopSpec[] Stops);

    private const OperatingDays Weekdays = OperatingDays.Monday | OperatingDays.Tuesday | OperatingDays.Wednesday
        | OperatingDays.Thursday | OperatingDays.Friday;

    private static readonly (string Code, string En, string Si, string Ta)[] Stations =
    [
        ("FOT", "Colombo Fort", "කොළඹ කොටුව", "கொழும்பு கோட்டை"),
        ("MDA", "Maradana", "මරදාන", "மருதானை"),
        ("RGM", "Ragama", "රාගම", "ராகமை"),
        ("GPH", "Gampaha", "ගම්පහ", "கம்பஹா"),
        ("VEY", "Veyangoda", "වේයන්ගොඩ", "வேயங்கொடை"),
        // The two Grilled collection points. Their IDs are copied into services/grilled/src/seed.ts.
        ("PLG", "Polgahawela Junction", "පොල්ගහවෙල හන්දිය", "பொல்கஹவெல சந்தி"),
        ("PDN", "Peradeniya Junction", "පේරාදෙණිය හන්දිය", "பேராதனை சந்தி"),
    ];

    private static readonly PatternSpec[] Patterns =
    [
        new("SIM-1001", Weekdays,
        [
            new("GPH", null, "07:35", Platform: "2"),
            new("RGM", "07:50", "07:51", Platform: "1"),
            new("MDA", "08:20", "08:22", Platform: "3"),
            new("FOT", "08:30", null, Platform: "4"),
        ]),
        new("SIM-1002", Weekdays,
        [
            new("GPH", null, "08:05", Platform: "2"),
            new("RGM", "08:20", "08:21", Platform: "1"),
            new("MDA", "08:50", "08:52", Platform: "3"),
            new("FOT", "09:00", null, Platform: "5"),
        ]),
        new("SIM-1003", OperatingDays.Every,
        [
            new("GPH", null, "07:20", Platform: "1"),
            new("RGM", "07:35", "07:36", Platform: "2"),
            new("FOT", "08:15", null, Platform: "4"),
        ]),
        new("SIM-2001", OperatingDays.Every,
        [
            new("FOT", null, "17:10", Platform: "6"),
            new("MDA", "17:18", "17:20", Platform: "2"),
            new("RGM", "17:55", "17:56", Platform: "1"),
            new("GPH", "18:15", "18:16", Platform: "2"),
            new("VEY", "18:30", null, Platform: "1"),
        ]),
        // The up-country corridor Grilled serves. It ends at Peradeniya, so ending it early at
        // Polgahawela leaves a food pickup beyond the new final stop.
        new("SIM-3001", OperatingDays.Every,
        [
            new("FOT", null, "09:00", Platform: "6"),
            new("RGM", "09:25", "09:26", Platform: "1"),
            new("GPH", "09:45", "09:46", Platform: "2"),
            new("VEY", "10:05", "10:06", Platform: "1"),
            new("PLG", "11:00", "11:05", Platform: "3"),
            new("PDN", "11:50", null, Platform: "1"),
        ]),
        // Crosses midnight: day offset 1 puts the later stops on the next calendar day.
        new("SIM-9001", OperatingDays.Every,
        [
            new("FOT", null, "23:45", Platform: "6"),
            new("RGM", "00:25", "00:26", 1, "1"),
            new("GPH", "00:45", null, 1, "2"),
        ]),
    ];

    public static async Task SeedAsync(
        SmartRailDbContext db, DateOnly startDate, int days, CancellationToken cancellationToken = default)
    {
        var stationIds = new Dictionary<string, Guid>();
        var existingStations = await db.Stations.ToDictionaryAsync(s => s.Code, cancellationToken);
        foreach (var (code, en, si, ta) in Stations)
        {
            if (!existingStations.TryGetValue(code, out var station))
            {
                station = new Station { Id = StableId("station", code), Code = code, NameEn = en, NameSi = si, NameTa = ta };
                db.Stations.Add(station);
            }

            stationIds[code] = station.Id;
        }

        var patternIds = Patterns.Select(p => StableId("pattern", p.TrainNumber)).ToList();
        var existingPatterns = await db.ServicePatterns
            .Where(p => patternIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var patterns = new List<ServicePattern>();
        foreach (var spec in Patterns)
        {
            var id = StableId("pattern", spec.TrainNumber);
            if (!existingPatterns.TryGetValue(id, out var pattern))
            {
                pattern = new ServicePattern
                {
                    Id = id,
                    TrainNumber = spec.TrainNumber,
                    SourceKind = SourceKind.FictionalDemo,
                    OperatingDays = spec.Days,
                    EffectiveFrom = startDate,
                    Stops = spec.Stops.Select((stop, index) => new PatternStop
                    {
                        Id = StableId("stop", $"{spec.TrainNumber}:{index + 1}"),
                        PatternId = id,
                        StationId = stationIds[stop.Station],
                        Sequence = index + 1,
                        ArrivalLocal = stop.Arrival is null ? null : TimeOnly.Parse(stop.Arrival),
                        DepartureLocal = stop.Departure is null ? null : TimeOnly.Parse(stop.Departure),
                        DayOffset = stop.DayOffset,
                        PlannedPlatform = stop.Platform,
                    }).ToList(),
                };
                db.ServicePatterns.Add(pattern);
            }

            patterns.Add(pattern);
        }

        var keys = await db.Journeys
            .Where(j => j.DemoSeedKey != null)
            .Select(j => j.DemoSeedKey!)
            .ToListAsync(cancellationToken);
        var existingKeys = keys.ToHashSet();

        for (var offset = 0; offset < days; offset++)
        {
            var date = startDate.AddDays(offset);
            foreach (var pattern in patterns.Where(p => p.RunsOn(date)))
            {
                var key = $"{pattern.TrainNumber}:{date:yyyy-MM-dd}";
                if (existingKeys.Add(key))
                {
                    db.Journeys.Add(new ScheduledJourney
                    {
                        Id = StableId("journey", key),
                        PatternId = pattern.Id,
                        ServiceDate = date,
                        DemoSeedKey = key,
                        Pattern = pattern,
                    });
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Guid StableId(string kind, string key) => StableIds.For(kind, key);
}
