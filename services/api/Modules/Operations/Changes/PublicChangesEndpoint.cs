using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Operations.Changes;

// The confirmed state of a journey for anyone to read: what staff decided, not who or why.
// It carries no staff identity or audit data.
public static class PublicChangesEndpoint
{
    public static void MapPublicChanges(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/journeys/{journeyId:guid}/changes", Get).AllowAnonymous();

    private static async Task<IResult> Get(
        Guid journeyId, HttpContext context, IJourneyStopReader journeys, ChangeStore store)
    {
        var journey = await journeys.FindOperationsJourneyAsync(journeyId, context.RequestAborted);
        var changes = journey is null ? null : await store.LoadAsync(journeyId, context.RequestAborted);
        if (journey is null || changes is null)
        {
            return Results.Problem(title: "Unknown journey", detail: "No journey has this ID.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return ETagResults.JsonWithETag(context, ConfirmedStateDto.From(journey, changes.Version, changes.History));
    }
}

public sealed record ActiveChangeDto(
    Guid ChangeId, string Kind, DateTimeOffset ConfirmedAt,
    int? DelayMinutes, string? NewPlatform, Guid? TargetStopId, Guid? FinalStopId);

public sealed record ServedStopDto(
    Guid StopId, Guid StationId, int Sequence, bool Served,
    string? PlannedPlatform, string? ConfirmedPlatform, string? Platform);

public sealed record ConfirmedStateDto(
    Guid JourneyId, int JourneyVersion, string Availability, int? ConfirmedDelayMinutes, Guid? FinalStopId,
    bool OrderClosed, IReadOnlyList<ActiveChangeDto> ActiveChanges, IReadOnlyList<ServedStopDto> Stops)
{
    public static ConfirmedStateDto From(OperationsJourney journey, int version, IReadOnlyList<ServiceChange> history) =>
        From(journey, version, ConfirmedState.Fold(history));

    public static ConfirmedStateDto From(OperationsJourney journey, int version, ConfirmedState state)
    {
        var outcome = JourneyOutcome.From(journey, state);
        return new ConfirmedStateDto(
            journey.Id,
            version,
            outcome.Availability.ToCode(),
            outcome.ConfirmedDelayMinutes,
            outcome.FinalStopId,
            outcome.OrderClosed,
            state.Active.Select(c => new ActiveChangeDto(
                c.Id, c.Kind.ToCode(), c.PublishedAt, c.DelayMinutes, c.NewPlatform, c.TargetStopId, c.FinalStopId)).ToList(),
            outcome.Stops.Select(s => new ServedStopDto(
                s.StopId, s.StationId, s.Sequence, s.Served, s.PlannedPlatform, s.ConfirmedPlatform, s.Platform)).ToList());
    }
}
