using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Operations.Changes;

// Staff routes for reviewing a service change. A signed-in, enabled account is required, then
// the permission is rechecked from the database on every request.
public static class StaffChangeEndpoints
{
    public const string JourneyTarget = "JOURNEY";

    public static void MapStaffChanges(this IEndpointRouteBuilder app)
    {
        var journey = app.MapGroup("/api/operations/journeys/{journeyId:guid}")
            .RequireAuthorization(AccessPolicies.ActiveAccount);

        journey.MapPost("changes/preview", Preview);
    }

    // Shows what a publish or withdrawal would do. Nothing is stored, versioned or sent.
    private static async Task<IResult> Preview(
        Guid journeyId, ChangeRequest? request, HttpContext context,
        IJourneyStopReader journeys, ChangeStore store, AccessGuard guard)
    {
        var loaded = await LoadAsync(journeyId, request, context, journeys, store, guard);
        if (loaded.Failure is not null)
        {
            return loaded.Failure;
        }

        var preview = Evaluate(loaded.Journey!, loaded.Changes!, request!);
        return preview.Problem is { } problem
            ? ToProblem(problem)
            : Results.Ok(ChangePreviewDto.From(preview.Value!));
    }

    // The common start of preview and confirm: the journey exists, the body names exactly one
    // action, and the caller may do it. The caller gets either the journey with its version and
    // history, or the response to send.
    internal static async Task<Loaded> LoadAsync(
        Guid journeyId, ChangeRequest? request, HttpContext context,
        IJourneyStopReader journeys, ChangeStore store, AccessGuard guard)
    {
        var journey = await journeys.FindOperationsJourneyAsync(journeyId, context.RequestAborted);
        var changes = journey is null ? null : await store.LoadAsync(journeyId, context.RequestAborted);
        if (journey is null || changes is null)
        {
            return Loaded.Fail(Results.Problem(
                title: "Unknown journey", detail: "No journey has this ID.", statusCode: StatusCodes.Status404NotFound));
        }

        if (request is null || (request.Draft is null) == (request.WithdrawChangeId is null))
        {
            return Loaded.Fail(Results.Problem(
                title: "Invalid change request",
                detail: "Send either draft or withdrawChangeId.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        var decision = await guard.CheckAnyAsync(
            context, PermissionOptions(journey, changes.History, request), JourneyTarget, journeyId);
        return decision == AccessDecision.Allowed
            ? Loaded.Ok(journey, changes)
            : Loaded.Fail(Results.Forbid());
    }

    // A controller may change anything. A platform change may instead come from station staff
    // granted the station of the stop it targets, which the server finds from the stop itself.
    private static List<(string Permission, AccessTarget Target)> PermissionOptions(
        OperationsJourney journey, IReadOnlyList<ServiceChange> history, ChangeRequest request)
    {
        var options = new List<(string, AccessTarget)> { (Permissions.ChangesPublish, new AccessTarget.Network()) };

        var platformStop = request.Draft is { Kind: "PLATFORM", TargetStopId: { } draftStop }
            ? draftStop
            : request.WithdrawChangeId is { } id
                && history.FirstOrDefault(c => c.Id == id) is { Action: ChangeAction.Publish, Kind: ChangeKind.Platform } platform
                ? platform.TargetStopId
                : null;

        if (platformStop is { } stop && journey.IndexOf(stop) is var index and >= 0)
        {
            options.Add((Permissions.PlatformChangesPublish, new AccessTarget.Station(journey.Stops[index].StationId)));
        }

        return options;
    }

    internal static Outcome<ChangePreview> Evaluate(OperationsJourney journey, JourneyChanges changes, ChangeRequest request) =>
        request.Draft is not null
            ? ChangePreviewer.PreviewPublish(journey, changes.Version, changes.History, request.Draft)
            : ChangePreviewer.PreviewWithdrawal(journey, changes.Version, changes.History, request.WithdrawChangeId);

    internal static IResult ToProblem(ChangeProblem problem) =>
        problem.Kind == ProblemKind.NotFound
            ? Results.Problem(title: "Unknown change", detail: problem.Message, statusCode: StatusCodes.Status404NotFound)
            : Results.Problem(title: "Invalid change", detail: problem.Message, statusCode: StatusCodes.Status400BadRequest);

    internal sealed record Loaded(IResult? Failure, OperationsJourney? Journey, JourneyChanges? Changes)
    {
        public static Loaded Ok(OperationsJourney journey, JourneyChanges changes) => new(null, journey, changes);

        public static Loaded Fail(IResult failure) => new(failure, null, null);
    }
}
