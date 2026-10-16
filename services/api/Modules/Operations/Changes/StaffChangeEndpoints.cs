using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Operations.Changes;

// Staff routes for reviewing a service change. A signed-in, enabled account is required, then
// the permission is rechecked from the database on every request.
public static class StaffChangeEndpoints
{
    public const string JourneyTarget = "JOURNEY";

    public const int DefaultAuditLimit = 100;
    public const int MaxAuditLimit = 200;

    public static void MapStaffChanges(this IEndpointRouteBuilder app)
    {
        var journey = app.MapGroup("/api/operations/journeys/{journeyId:guid}")
            .RequireAuthorization(AccessPolicies.ActiveAccount);

        journey.MapPost("changes/preview", Preview);
        journey.MapPost("changes", Confirm);

        // Reading the trail needs its own permission, held by auditors but not by controllers.
        journey.MapGet("audit", ReadAudit).RequireAuthorization(AccessPolicies.For(Permissions.AuditRead));
    }

    // Who did what to this journey and when, oldest first, at most limit entries. Denied
    // attempts are listed beside successful changes. Request bodies are never kept, so none show.
    private static async Task<IResult> ReadAudit(
        Guid journeyId, int? limit, HttpContext context, IJourneyStopReader journeys, IAuditTrail audit)
    {
        if (await journeys.FindAsync(journeyId, context.RequestAborted) is null)
        {
            return Results.Problem(
                title: "Unknown journey", detail: "No journey has this ID.", statusCode: StatusCodes.Status404NotFound);
        }

        var take = Math.Clamp(limit ?? DefaultAuditLimit, 1, MaxAuditLimit);
        var entries = await audit.ReadForTargetAsync(JourneyTarget, journeyId, take, context.RequestAborted);
        return Results.Ok(new JourneyAuditDto(journeyId, entries));
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

    // Makes the previewed change real, but only if the journey is still at the version the
    // preview showed. The preview is recomputed here; nothing the client says about its impact is
    // trusted. A stale preview is a 409 and nothing is written.
    private static async Task<IResult> Confirm(
        Guid journeyId, ChangeRequest? request, HttpContext context,
        IJourneyStopReader journeys, ChangeStore store, AccessGuard guard, CurrentAccount current, ChangeConfirmer confirmer)
    {
        var loaded = await LoadAsync(journeyId, request, context, journeys, store, guard);
        if (loaded.Failure is not null)
        {
            return loaded.Failure;
        }

        var journey = loaded.Journey!;
        var changes = loaded.Changes!;
        if (request!.PreviewedVersion is not { } previewedVersion)
        {
            return Results.Problem(
                title: "Invalid change request",
                detail: "previewedVersion is required: preview the change, then confirm it.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Problems with the draft itself do not depend on the journey's history.
        if (request.Draft is not null && ChangeRules.ValidateDraft(request.Draft, journey).Problem is { } invalid)
        {
            return ToProblem(invalid);
        }

        if (previewedVersion != changes.Version)
        {
            return StalePreview(previewedVersion, changes.Version);
        }

        var preview = Evaluate(journey, changes, request);
        if (preview.Problem is { } problem)
        {
            return ToProblem(problem);
        }

        var actor = (await current.GetAsync(context.User, context.RequestAborted))!;
        var result = await confirmer.ConfirmAsync(context, actor.Id, journey, changes, preview.Value!);
        return result switch
        {
            ConfirmResult.Confirmed done => Results.Ok(new ChangeConfirmedDto(done.ChangeId, done.JourneyVersion, done.State)),
            ConfirmResult.Stale stale => StalePreview(previewedVersion, stale.LatestVersion),
            _ => throw new InvalidOperationException("Unknown confirmation result."),
        };
    }

    // 409 with the latest version, so the client previews again. It is never retried here.
    private static IResult StalePreview(int previewedVersion, int latestVersion) =>
        Results.Problem(
            title: "Stale preview",
            detail: "The journey changed after this preview. Preview it again before confirming.",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "STALE_PREVIEW",
                ["previewedVersion"] = previewedVersion,
                ["latestVersion"] = latestVersion,
            });

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
