using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Rail.Running;

// The next trains between two stations with the running picture applied: a delayed train stays
// findable after its scheduled time, and a cancelled one is a notice, not a choice. The scheduled
// GET /api/next-trains keeps its own meaning.
public static class LiveNextTrainsEndpoint
{
    public static void MapLiveNextTrains(this IEndpointRouteBuilder app) =>
        app.MapGroup("/api").AllowAnonymous().MapGet("/live-next-trains", Get);

    private static async Task<IResult> Get(
        HttpContext context, SmartRailDbContext db, TimeProvider time, IConfirmedChangeReader changes,
        string? originId, string? destinationId)
    {
        if (!Guid.TryParse(originId, out var origin) || !Guid.TryParse(destinationId, out var destination))
        {
            return Bad("Invalid station", "originId and destinationId are required and must be station IDs.");
        }

        if (origin == destination)
        {
            return Bad("Invalid route", "originId and destinationId must be different stations.");
        }

        var knownStations = await db.Stations.CountAsync(s => s.Id == origin || s.Id == destination, context.RequestAborted);
        if (knownStations != 2)
        {
            return Results.Problem(title: "Unknown station", detail: "Origin or destination is not a known station.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var now = time.GetUtcNow();
        var today = ColomboTime.ServiceDateOf(now);
        var from = today.AddDays(-RailEndpoints.ServiceDatesBack);
        var to = today.AddDays(RailEndpoints.ServiceDatesAhead);
        var journeys = await db.Journeys
            .AsNoTracking()
            .Include(j => j.Pattern).ThenInclude(p => p.Stops)
            .Where(j => j.ServiceDate >= from && j.ServiceDate <= to)
            .Where(j => j.Pattern.Stops.Any(s => s.StationId == origin) && j.Pattern.Stops.Any(s => s.StationId == destination))
            .ToListAsync(context.RequestAborted);

        // Observations and confirmed changes are read only for journeys that really match the route.
        var live = new List<LiveJourney>();
        foreach (var journey in journeys.Where(j => NextTrainFinder.Matches(j, origin, destination).Count > 0))
        {
            var observation = await ObservationIngestion.CurrentAsync(db, journey.Id, context.RequestAborted);
            var confirmed = await changes.ReadAsync(journey.Id, context.RequestAborted);
            live.Add(new LiveJourney(journey, observation?.ToRunning(), confirmed));
        }

        var result = LiveNextTrains.Find(live, origin, destination, now);

        // Set before the ETag helper so a 304 carries it too. It is not part of the body.
        context.Response.Headers[RunningEndpoints.ServerTimeHeader] =
            now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        return ETagResults.JsonWithETag(context, new LiveNextTrainsResponse(
            origin,
            destination,
            RunningResponses.RunningLabel,
            new FreshnessThresholds(FreshnessRules.AgeingAfterSeconds, FreshnessRules.UnknownAfterSeconds),
            result.Trains,
            result.Notices));
    }

    private static IResult Bad(string title, string detail) =>
        Results.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private sealed record LiveNextTrainsResponse(
        Guid OriginId, Guid DestinationId, string RunningLabel, FreshnessThresholds Thresholds,
        IReadOnlyList<LiveTrain> Trains, IReadOnlyList<LiveNotice> Notices);
}
