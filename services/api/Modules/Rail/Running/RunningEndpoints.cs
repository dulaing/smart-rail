using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Rail.Running;

// Simulated running. Writes are internal and need the service key; no account can post here.
public static class RunningEndpoints
{
    public const string ObservationsPath = "/internal/rail/observations";

    public static void MapRunningEndpoints(this IEndpointRouteBuilder app)
    {
        // AllowAnonymous only skips the signed-in-user fallback; the service key is the guard.
        app.MapPost(ObservationsPath, PostObservation)
            .AllowAnonymous()
            .AddEndpointFilter<ServiceKeyFilter>();
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
