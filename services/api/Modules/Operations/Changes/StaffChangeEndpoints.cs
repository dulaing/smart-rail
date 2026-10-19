using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Incidents;
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

    // Who did what to this journey and when, oldest first, one bounded page at a time. Denied
    // attempts are listed beside successful changes, and so are the journey's driver reports.
    // Request bodies are never kept, so none show. The optional cursor continues from an earlier
    // page; nextCursor is null at the end of the trail.
    private static async Task<IResult> ReadAudit(
        Guid journeyId, int? limit, string? cursor, HttpContext context,
        IJourneyStopReader journeys, IAuditTrail audit, JourneyAuditTargets targets)
    {
        if (await journeys.FindAsync(journeyId, context.RequestAborted) is null)
        {
            return Results.Problem(
                title: "Unknown journey", detail: "No journey has this ID.", statusCode: StatusCodes.Status404NotFound);
        }

        PageCursor? after = null;
        if (cursor is not null)
        {
            if (!PageCursor.TryDecode(cursor, out var decoded))
            {
                return Results.Problem(
                    title: "Invalid audit request",
                    detail: "cursor is not a continuation token from this list.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            after = decoded;
        }

        var take = Math.Clamp(limit ?? DefaultAuditLimit, 1, MaxAuditLimit);
        var page = await audit.ReadPageAsync(
            await targets.ForJourneyAsync(journeyId, context.RequestAborted),
            after?.Time, after?.Id, take, context.RequestAborted);
        var next = page.HasMore ? new PageCursor(page.Entries[^1].OccurredAt, page.Entries[^1].Id).Encode() : null;
        return Results.Ok(new JourneyAuditDto(journeyId, page.Entries, next, !page.HasMore));
    }

    // Shows what a publish or withdrawal would do, including the incident it would carry.
    // Nothing is stored, versioned or sent.
    private static async Task<IResult> Preview(
        Guid journeyId, ChangeRequest? request, HttpContext context,
        IJourneyStopReader journeys, ChangeStore store, AccessGuard guard, IncidentLinks links)
    {
        var loaded = await LoadAsync(journeyId, request, context, journeys, store, guard);
        if (loaded.Failure is not null)
        {
            return loaded.Failure;
        }

        var preview = Evaluate(loaded.Journey!, loaded.Changes!, request!);
        if (preview.Problem is { } problem)
        {
            return ToProblem(problem);
        }

        var link = await LinkAsync(journeyId, request!, loaded.Changes!, preview.Value!, links, context.RequestAborted);
        return link.Failure ?? Results.Ok(ChangePreviewDto.From(preview.Value!, link.IncidentId));
    }

    // Makes the previewed change real, but only if the journey is still at the version the
    // preview showed. The preview is recomputed here; nothing the client says about its impact is
    // trusted. A stale preview is a 409 and nothing is written.
    private static async Task<IResult> Confirm(
        Guid journeyId, ChangeRequest? request, HttpContext context,
        IJourneyStopReader journeys, ChangeStore store, AccessGuard guard, CurrentAccount current, ChangeConfirmer confirmer,
        IncidentLinks links)
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

        // The incident is checked again now; the confirmer holds it open while it commits.
        var link = await LinkAsync(journeyId, request, changes, preview.Value!, links, context.RequestAborted);
        if (link.Failure is not null)
        {
            return link.Failure;
        }

        var actor = (await current.GetAsync(context.User, context.RequestAborted))!;
        var result = await confirmer.ConfirmAsync(context, actor.Id, journey, changes, preview.Value!, link.IncidentId);
        return result switch
        {
            ConfirmResult.Confirmed done => Results.Ok(new ChangeConfirmedDto(done.ChangeId, done.JourneyVersion, done.State, done.IncidentId)),
            ConfirmResult.Stale stale => StalePreview(previewedVersion, stale.LatestVersion),
            ConfirmResult.IncidentClosed => LinkProblem(new ChangeProblem(
                ProblemKind.Invalid, "The incident was resolved after the preview, so it can no longer explain this change.")),
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

        if (request.IncidentId is { } incidentId && (request.WithdrawChangeId is not null || incidentId == Guid.Empty))
        {
            return Loaded.Fail(Results.Problem(
                title: "Invalid change request",
                detail: request.WithdrawChangeId is not null
                    ? "A withdrawal keeps the incident of the change it withdraws; do not send incidentId."
                    : "incidentId must be an incident ID.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        var decision = await guard.CheckAnyAsync(
            context, PermissionOptions(journey, changes.History, request), JourneyTarget, journeyId);

        // Naming an incident also takes the right to manage incidents, which station staff lack.
        if (decision == AccessDecision.Allowed && request.IncidentId is not null)
        {
            decision = await guard.CheckAsync(context, Permissions.IncidentsManage, new AccessTarget.Network());
        }

        return decision == AccessDecision.Allowed
            ? Loaded.Ok(journey, changes)
            : Loaded.Fail(Results.Forbid());
    }

    // The incident a change would carry. A new publish carries the one it names, once that checks
    // out; a withdrawal carries whatever the change it withdraws already has.
    private static async Task<(Guid? IncidentId, IResult? Failure)> LinkAsync(
        Guid journeyId, ChangeRequest request, JourneyChanges changes, ChangePreview preview,
        IncidentLinks links, CancellationToken cancellationToken)
    {
        if (preview.Action == ChangeAction.Withdraw)
        {
            return (changes.History.FirstOrDefault(c => c.Id == preview.WithdrawsChangeId)?.IncidentId, null);
        }

        if (request.IncidentId is not { } incidentId)
        {
            return (null, null);
        }

        return await links.CheckForPublishAsync(journeyId, incidentId, cancellationToken) is { } problem
            ? (null, LinkProblem(problem))
            : (incidentId, null);
    }

    private static IResult LinkProblem(ChangeProblem problem) =>
        problem.Kind == ProblemKind.NotFound
            ? Results.Problem(title: "Unknown incident", detail: problem.Message, statusCode: StatusCodes.Status404NotFound)
            : Results.Problem(title: "Invalid incident link", detail: problem.Message, statusCode: StatusCodes.Status400BadRequest);

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
