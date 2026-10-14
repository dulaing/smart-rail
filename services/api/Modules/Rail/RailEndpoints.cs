using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Rail;

/// <summary>Anonymous timetable reads. No sign-in is needed for any route here.</summary>
public static partial class RailEndpoints
{
    public const string ScheduleNote = "Scheduled times only — not live";

    // An overnight train that left up to two service dates ago can still be running, and the
    // next service date supplies trains when none are left today.
    private const int ServiceDatesBack = 2;
    private const int ServiceDatesAhead = 1;

    public static void MapRailEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/stations", GetStations);
        api.MapGet("/journeys", GetJourneys);
        api.MapGet("/next-trains", GetNextTrains);
    }

    private static async Task<IResult> GetStations(HttpContext context, SmartRailDbContext db)
    {
        var stations = await db.Stations
            .AsNoTracking()
            .OrderBy(s => s.Code)
            .Select(s => new StationDto(s.Id, s.Code, s.NameEn, s.NameSi, s.NameTa))
            .ToListAsync(context.RequestAborted);

        return ETagResults.JsonWithETag(context, stations);
    }

    private static async Task<IResult> GetJourneys(HttpContext context, SmartRailDbContext db, string? serviceDate)
    {
        if (!DateOnly.TryParseExact(serviceDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return Bad("Invalid serviceDate", "serviceDate is required and must be a date like 2026-10-14.");
        }

        var journeys = await db.Journeys
            .AsNoTracking()
            .Include(j => j.Pattern).ThenInclude(p => p.Stops)
            .Where(j => j.ServiceDate == date)
            .ToListAsync(context.RequestAborted);

        var stationCodes = await db.Stations.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Code, context.RequestAborted);

        var items = journeys
            .Where(j => j.Pattern.RunsOn(j.ServiceDate))
            .OrderBy(j => j.Pattern.TrainNumber, StringComparer.Ordinal)
            .ThenBy(j => j.Id)
            .Select(j => new JourneyDto(
                j.Id,
                j.ServiceDate,
                j.Pattern.TrainNumber,
                j.Pattern.SourceKind.ToCode(),
                j.Pattern.SourceKind.Label(),
                j.Pattern.SourceUrl,
                j.Pattern.SourceCheckedAt,
                j.Pattern.Stops.OrderBy(s => s.Sequence).Select(s => new JourneyStopDto(
                    s.Id,
                    s.Sequence,
                    s.StationId,
                    stationCodes.GetValueOrDefault(s.StationId, ""),
                    s.ArrivalLocal is { } a ? ColomboTime.ToInstant(j.ServiceDate, a, s.DayOffset) : null,
                    s.DepartureLocal is { } d ? ColomboTime.ToInstant(j.ServiceDate, d, s.DayOffset) : null,
                    s.PlannedPlatform)).ToList()))
            .ToList();

        return ETagResults.JsonWithETag(context, new JourneysResponse(date, ScheduleNote, items));
    }

    private static async Task<IResult> GetNextTrains(
        HttpContext context, SmartRailDbContext db, TimeProvider time, string? originId, string? destinationId, string? at)
    {
        if (!Guid.TryParse(originId, out var origin) || !Guid.TryParse(destinationId, out var destination))
        {
            return Bad("Invalid station", "originId and destinationId are required and must be station IDs.");
        }

        if (origin == destination)
        {
            return Bad("Invalid route", "originId and destinationId must be different stations.");
        }

        DateTimeOffset instant;
        if (string.IsNullOrWhiteSpace(at))
        {
            instant = time.GetUtcNow();
        }
        else if (!TryParseInstant(at, out instant))
        {
            return Bad("Invalid at", "at must be an ISO 8601 timestamp with an offset, such as 2026-10-14T07:00:00+05:30.");
        }

        var knownStations = await db.Stations.CountAsync(s => s.Id == origin || s.Id == destination, context.RequestAborted);
        if (knownStations != 2)
        {
            return Results.Problem(title: "Unknown station", detail: "Origin or destination is not a known station.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var today = ColomboTime.ServiceDateOf(instant);
        var from = today.AddDays(-ServiceDatesBack);
        var to = today.AddDays(ServiceDatesAhead);
        var journeys = await db.Journeys
            .AsNoTracking()
            .Include(j => j.Pattern).ThenInclude(p => p.Stops)
            .Where(j => j.ServiceDate >= from && j.ServiceDate <= to)
            .Where(j => j.Pattern.Stops.Any(s => s.StationId == origin) && j.Pattern.Stops.Any(s => s.StationId == destination))
            .ToListAsync(context.RequestAborted);

        var trains = NextTrainFinder.Find(journeys, origin, destination, instant)
            .Select(t => new NextTrainDto(
                t.JourneyId,
                t.ServiceDate,
                t.TrainNumber,
                t.SourceKind.ToCode(),
                t.SourceKind.Label(),
                t.SourceUrl,
                t.SourceCheckedAt,
                t.OriginStopId,
                t.OriginArrival,
                t.OriginDeparture,
                t.PlannedPlatform,
                t.DestinationStopId,
                t.DestinationArrival))
            .ToList();

        return ETagResults.JsonWithETag(context, new NextTrainsResponse(origin, destination, ScheduleNote, trains));
    }

    private static IResult Bad(string title, string detail) =>
        Results.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);

    // The offset must be written out ("Z" or "+05:30"); a bare local time would be a guess.
    private static bool TryParseInstant(string value, out DateTimeOffset instant)
    {
        instant = default;
        return OffsetSuffix().IsMatch(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out instant);
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T.*(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex OffsetSuffix();

    private sealed record StationDto(Guid Id, string Code, string NameEn, string NameSi, string NameTa);

    private sealed record JourneyStopDto(
        Guid StopId, int Sequence, Guid StationId, string StationCode,
        DateTimeOffset? ScheduledArrival, DateTimeOffset? ScheduledDeparture, string? PlannedPlatform);

    private sealed record JourneyDto(
        Guid JourneyId, DateOnly ServiceDate, string TrainNumber,
        string SourceKind, string SourceLabel, string? SourceUrl, DateTimeOffset? SourceCheckedAt,
        IReadOnlyList<JourneyStopDto> Stops);

    private sealed record JourneysResponse(DateOnly ServiceDate, string ScheduleNote, IReadOnlyList<JourneyDto> Journeys);

    private sealed record NextTrainDto(
        Guid JourneyId, DateOnly ServiceDate, string TrainNumber,
        string SourceKind, string SourceLabel, string? SourceUrl, DateTimeOffset? SourceCheckedAt,
        Guid OriginStopId, DateTimeOffset? OriginScheduledArrival, DateTimeOffset OriginScheduledDeparture,
        string? PlannedPlatform, Guid DestinationStopId, DateTimeOffset DestinationScheduledArrival);

    private sealed record NextTrainsResponse(
        Guid OriginId, Guid DestinationId, string ScheduleNote, IReadOnlyList<NextTrainDto> Trains);
}
