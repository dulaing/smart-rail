using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Infrastructure;

// The repeatable pilot scenario: which dated demo journeys the presentation uses, on which
// Colombo service date, and the stable IDs a rehearsal or test looks up. It reuses the ordinary
// demo seed (so the same anchor adds nothing) and only adds the anchor rule and the manifest.
public static class PilotScenario
{
    // The two routes the scenario names. Both run every day, so any anchor date works. The
    // opening route carries guests Gampaha -> Fort; the food route runs outbound Fort -> Peradeniya
    // through Polgahawela, so ending it early at Polgahawela leaves Peradeniya unserved.
    public const string GuestOpeningTrain = "SIM-1003";
    public const string OutboundFoodTrain = "SIM-3001";
    public const string EarlyEndFrom = "PLG";
    public const string EarlyEndTo = "PDN";

    public const string GuestOpeningRole = "guest-opening";
    public const string OutboundFoodRole = "outbound-food";
    public const string SupportingRole = "supporting";

    // Stations the provider (Grilled) serves. SmartRail never owns their menu, stock or cutoff;
    // the manifest only names the station so the provider's own seed can be matched to it.
    private static readonly string[] CollectionPointStations = ["PLG", "PDN"];

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static DateOnly ServiceDateOf(DateTimeOffset anchor) => ColomboTime.ServiceDateOf(anchor);

    // An anchor must carry its offset, so a bare local time cannot silently mean another instant.
    public static bool TryParseAnchor(string? raw, out DateTimeOffset anchor, out string? error)
    {
        anchor = default;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "--anchor needs an offset-bearing ISO time, for example 2026-10-20T08:00:00+05:30.";
            return false;
        }

        var value = raw.Trim();
        var afterDate = value.IndexOf('T');
        var hasOffset = value.EndsWith('Z') || value.EndsWith('z')
            || (afterDate >= 0 && (value.LastIndexOf('+') > afterDate || value.LastIndexOf('-') > afterDate));
        if (!hasOffset)
        {
            error = $"'{value}' has no UTC offset. Use an offset-bearing ISO time, for example 2026-10-20T08:00:00+05:30.";
            return false;
        }

        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out anchor))
        {
            error = $"'{value}' is not a valid offset-bearing ISO time.";
            return false;
        }

        return true;
    }

    public static string DefaultRun(DateTimeOffset anchor) =>
        "anchor-" + anchor.ToOffset(ColomboTime.Offset).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    public static string SafeRun(string run)
    {
        var safe = new string(run.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "run" : safe;
    }

    // Reads the seeded rows for the anchor's service date, so the manifest always describes what
    // is actually in the database rather than a second copy of the timetable.
    public static async Task<ScenarioManifest> BuildManifestAsync(
        SmartRailDbContext db, DateTimeOffset anchor, string run, CancellationToken cancellationToken = default)
    {
        var serviceDate = ServiceDateOf(anchor);
        var stationCodes = await db.Stations.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Code, cancellationToken);

        var journeys = await db.Journeys.AsNoTracking()
            .Include(j => j.Pattern).ThenInclude(p => p.Stops)
            .Where(j => j.DemoSeedKey != null && j.ServiceDate == serviceDate)
            .ToListAsync(cancellationToken);

        var entries = journeys
            .OrderBy(j => j.Pattern.TrainNumber, StringComparer.Ordinal)
            .Select(j => Describe(j, stationCodes))
            .ToList();

        var collectionPoints = stationCodes
            .Where(pair => CollectionPointStations.Contains(pair.Value))
            .OrderBy(pair => Array.IndexOf(CollectionPointStations, pair.Value))
            .Select(pair => new ScenarioCollectionPoint(pair.Value, pair.Key))
            .ToList();

        return new ScenarioManifest(anchor, run, serviceDate, entries, DescribeIdentities(), collectionPoints);
    }

    public static string ToJson(ScenarioManifest manifest) => JsonSerializer.Serialize(manifest, ManifestJson);

    public static async Task WriteManifestAsync(
        string path, ScenarioManifest manifest, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, ToJson(manifest), cancellationToken);
    }

    // Prints the IDs a rehearsal needs. There are no passwords or tokens in a manifest.
    public static void PrintSummary(ScenarioManifest manifest, string manifestPath)
    {
        Console.WriteLine(
            $"Pilot scenario: service date {manifest.ServiceDate:yyyy-MM-dd}, anchor {manifest.Anchor:O}, run '{manifest.Run}'.");
        foreach (var journey in manifest.Journeys)
        {
            var route = string.Join(" -> ", journey.Stops.Select(s => s.StationCode));
            Console.WriteLine($"  [{journey.Role}] {journey.TrainNumber} journey {journey.JourneyId}: {route}");
            foreach (var stop in journey.Stops)
            {
                Console.WriteLine(
                    $"      stop {stop.Sequence} {stop.StationCode} id {stop.StopId} (station {stop.StationId}) platform {stop.PlannedPlatform ?? "-"}");
            }

            if (journey.EarlyEndStop is { } earlyEnd)
            {
                Console.WriteLine(
                    $"      early end at {earlyEnd}; unserved afterwards: {string.Join(", ", journey.UnservedAfterEarlyEnd)}");
            }
        }

        Console.WriteLine("Intended identities (create with --seed-demo-identities):");
        foreach (var identity in manifest.Identities)
        {
            var scope = identity.StationCode is { } station ? $" station {station}"
                : identity.AssignedTrain is { } train ? $" train {train}"
                : string.Empty;
            Console.WriteLine($"  {identity.Username} ({identity.Role}){scope}");
        }

        Console.WriteLine("Grilled collection points (provider-owned; seed with services/grilled):");
        foreach (var point in manifest.CollectionPoints)
        {
            Console.WriteLine($"  {point.StationCode} station {point.StationId}");
        }

        Console.WriteLine($"Manifest written to {manifestPath}");
    }

    private static ScenarioJourney Describe(
        ScheduledJourney journey, IReadOnlyDictionary<Guid, string> stationCodes)
    {
        var stops = journey.Pattern.Stops.OrderBy(s => s.Sequence).Select(stop =>
        {
            var (arrival, departure) = ColomboTime.StopInstants(journey.ServiceDate, stop);
            return new ScenarioStop(
                stop.Sequence,
                stationCodes[stop.StationId],
                stop.StationId,
                stop.Id,
                arrival,
                departure,
                stop.PlannedPlatform);
        }).ToList();

        var codes = stops.Select(s => s.StationCode).ToList();
        var role = RoleOf(codes);
        var earlyEnd = role == OutboundFoodRole ? EarlyEndFrom : null;
        IReadOnlyList<string> unserved = role == OutboundFoodRole ? [EarlyEndTo] : [];

        return new ScenarioJourney(
            role, journey.Pattern.TrainNumber, journey.ServiceDate, journey.Id,
            codes[0], codes[^1], earlyEnd, unserved, stops);
    }

    private static string RoleOf(List<string> codes)
    {
        var gampaha = codes.IndexOf("GPH");
        var fort = codes.IndexOf("FOT");
        var polgahawela = codes.IndexOf("PLG");
        var peradeniya = codes.IndexOf("PDN");

        if (gampaha >= 0 && fort > gampaha && polgahawela < 0 && peradeniya < 0)
        {
            return GuestOpeningRole;
        }

        if (fort >= 0 && peradeniya > fort)
        {
            return OutboundFoodRole;
        }

        return SupportingRole;
    }

    private static IReadOnlyList<ScenarioIdentity> DescribeIdentities() =>
        DemoIdentitySeeder.Users
            .Select(user => new ScenarioIdentity(
                user.Username,
                user.Role,
                user.StationCode,
                user.Role == Roles.Driver ? DemoIdentitySeeder.DriverTrain : null))
            .ToList();
}

public sealed record ScenarioStop(
    int Sequence,
    string StationCode,
    Guid StationId,
    Guid StopId,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture,
    string? PlannedPlatform);

public sealed record ScenarioJourney(
    string Role,
    string TrainNumber,
    DateOnly ServiceDate,
    Guid JourneyId,
    string Origin,
    string Destination,
    string? EarlyEndStop,
    IReadOnlyList<string> UnservedAfterEarlyEnd,
    IReadOnlyList<ScenarioStop> Stops);

public sealed record ScenarioIdentity(string Username, string Role, string? StationCode, string? AssignedTrain);

public sealed record ScenarioCollectionPoint(string StationCode, Guid StationId);

public sealed record ScenarioManifest(
    DateTimeOffset Anchor,
    string Run,
    DateOnly ServiceDate,
    IReadOnlyList<ScenarioJourney> Journeys,
    IReadOnlyList<ScenarioIdentity> Identities,
    IReadOnlyList<ScenarioCollectionPoint> CollectionPoints);
