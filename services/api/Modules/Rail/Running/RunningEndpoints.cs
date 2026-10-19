using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Rail.Running;

// Simulated running. Guests read it; writes are internal and need the service key.
public static class RunningEndpoints
{
    public const string ObservationsPath = "/internal/rail/observations";

    // The server's clock on every 200 and 304, so clients can correct for a wrong device clock.
    public const string ServerTimeHeader = "X-SmartRail-Server-Time";

    public static void MapRunningEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api").AllowAnonymous().MapGet("/journeys/{journeyId}/running", GetRunning);
        app.MapLiveNextTrains();

        // AllowAnonymous only skips the signed-in-user fallback; the service key is the guard.
        app.MapPost(ObservationsPath, PostObservation)
            .AllowAnonymous()
            .AddEndpointFilter<ServiceKeyFilter>();
    }

    private static async Task<IResult> GetRunning(
        HttpContext context, SmartRailDbContext db, TimeProvider time, IConfirmedChangeReader changes, string journeyId)
    {
        if (!Guid.TryParse(journeyId, out var id))
        {
            return Results.Problem(title: "Invalid journeyId", detail: "journeyId must be a journey ID.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var journey = await db.Journeys
            .AsNoTracking()
            .Include(j => j.Pattern).ThenInclude(p => p.Stops)
            .SingleOrDefaultAsync(j => j.Id == id, context.RequestAborted);
        if (journey is null)
        {
            return Results.Problem(title: "Unknown journey", detail: "No journey has this ID.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var now = time.GetUtcNow();
        var observation = await ObservationIngestion.CurrentAsync(db, id, context.RequestAborted);
        var confirmed = await changes.ReadAsync(id, context.RequestAborted);
        var state = RunningCalculator.Calculate(journey, observation?.ToRunning(), now, overrides: confirmed);

        // Set before the ETag helper so a 304 carries it too. It is not part of the body.
        context.Response.Headers[ServerTimeHeader] =
            now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        return ETagResults.JsonWithETag(context, RunningResponses.Build(journey, state, observation));
    }

    private static async Task<IResult> PostObservation(
        HttpContext context, SmartRailDbContext db, TimeProvider time, ObservationRequest? body)
    {
        var result = await ObservationIngestion.IngestAsync(db, time, body, context.RequestAborted);

        return result.Status switch
        {
            IngestStatus.Created => Results.Json(Accepted(result.Observation!), statusCode: StatusCodes.Status201Created),
            IngestStatus.Duplicate => Results.Json(Accepted(result.Observation!), statusCode: StatusCodes.Status200OK),
            IngestStatus.Conflict => Results.Problem(
                title: "Observation conflict", detail: result.Detail, statusCode: StatusCodes.Status409Conflict),
            IngestStatus.UnknownJourney => Results.Problem(
                title: "Unknown journey", detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => Results.Problem(
                title: "Invalid observation", detail: result.Detail, statusCode: StatusCodes.Status400BadRequest),
        };
    }

    private static ObservationAccepted Accepted(Observation o) =>
        new(o.Id, o.SourceEventId, o.JourneyId, o.ObservedAt, o.ReceivedAt);

    private sealed record ObservationAccepted(
        Guid Id, string SourceEventId, Guid JourneyId, DateTimeOffset ObservedAt, DateTimeOffset ReceivedAt);
}
