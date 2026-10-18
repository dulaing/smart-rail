using Microsoft.Extensions.Caching.Memory;
using SmartRail.Api.Modules.Catering.Provider;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Catering.Orders;

// What a passenger can browse before signing in: where on this train they could collect food, and
// the menu there. SmartRail asks RailBites and adds the train's side (which stops are still ahead
// and served). Ordering itself needs an account; see OrderEndpoints.
public static class BrowseEndpoints
{
    // The menu may be shown from a cache this long. The cutoff never is: it is read from RailBites
    // on every request.
    private static readonly TimeSpan MenuTtl = TimeSpan.FromSeconds(60);

    public static void MapBrowse(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AllowAnonymous();
        api.MapGet("/journeys/{journeyId:guid}/collection-options", CollectionOptions);
        api.MapGet("/food/collection-points/{collectionPointId}/menu", Menu);
    }

    private static async Task<IResult> CollectionOptions(
        Guid journeyId, HttpContext context, TimeProvider time, IPickupReader pickups,
        IJourneyEligibilityReader eligibility, IRailBitesClient provider)
    {
        var now = time.GetUtcNow();
        var journey = await pickups.ReadAsync(journeyId, now, null, context.RequestAborted);
        var rules = await eligibility.ReadAsync(journeyId, context.RequestAborted);
        if (journey is null || rules is null)
        {
            return Problem(StatusCodes.Status404NotFound, "Unknown journey", "No journey has this ID.", OrderProblems.JourneyNotFound);
        }

        var points = await provider.ListCollectionPointsAsync(context.RequestAborted);
        if (points.Outcome != ProviderOutcome.Success)
        {
            return Unavailable();
        }

        var byStation = points.Value!.CollectionPoints.ToDictionary(p => p.SmartRailStationId);
        context.Response.Headers.CacheControl = "no-store";
        var options = journey.Stops
            .Where(s => s.ScheduledArrival is not null && byStation.ContainsKey(s.StationId))
            .OrderBy(s => s.Sequence)
            .Select(stop =>
            {
                var point = byStation[stop.StationId];
                var problem = PickupAssessor.ProblemFor(journey, rules, stop);
                return new CollectionOptionDto(
                    stop.StopId,
                    stop.StationId,
                    new CollectionPointDto(point.Id, point.Name, point.CutoffMinutes),
                    stop.ScheduledArrival,
                    stop.EstimatedArrival,
                    stop.EstimatedArrival?.AddMinutes(-point.CutoffMinutes),
                    problem is null,
                    problem);
            })
            .ToList();

        return Results.Ok(new CollectionOptionsDto(journey.JourneyId, journey.TrainNumber, journey.ServiceDate, options));
    }

    private static async Task<IResult> Menu(
        string collectionPointId, HttpContext context, IMemoryCache cache, IRailBitesClient provider)
    {
        if (!cache.TryGetValue(collectionPointId, out ProviderMenu? menu))
        {
            var response = await provider.GetMenuAsync(collectionPointId, context.RequestAborted);
            if (response.Outcome == ProviderOutcome.Refused && response.HttpStatus == StatusCodes.Status404NotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Unknown collection point", "No collection point has this ID.", "COLLECTION_POINT_NOT_FOUND");
            }

            if (response.Outcome != ProviderOutcome.Success)
            {
                return Unavailable();
            }

            menu = response.Value!;
            cache.Set(collectionPointId, menu, MenuTtl);
        }

        context.Response.Headers.CacheControl = $"public, max-age={(int)MenuTtl.TotalSeconds}";
        return Results.Ok(menu);
    }

    private static IResult Unavailable() =>
        Problem(StatusCodes.Status503ServiceUnavailable, "Food service unavailable",
            "The food provider could not be reached. Train information is unaffected.", "PROVIDER_UNAVAILABLE");

    private static IResult Problem(int status, string title, string detail, string code) =>
        Results.Problem(title: title, detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}

public sealed record CollectionPointDto(string Id, string Name, int CutoffMinutes);

// OrderBy is the latest moment an order is accepted, from the provider's cutoff. It moves with the ETA.
public sealed record CollectionOptionDto(
    Guid StopId,
    Guid StationId,
    CollectionPointDto CollectionPoint,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? EstimatedArrival,
    DateTimeOffset? OrderBy,
    bool Orderable,
    string? ReasonCode);

public sealed record CollectionOptionsDto(
    Guid JourneyId, string TrainNumber, DateOnly ServiceDate, IReadOnlyList<CollectionOptionDto> Options);
