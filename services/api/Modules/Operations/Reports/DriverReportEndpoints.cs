using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Operations.Reports;

// Driver routes for sending a field report and reading it back. A signed-in, enabled account is
// required, then the journey permission is rechecked from the database on every request: a driver
// who loses the grant or the assignment is refused on the next call. The driver is always the
// token's own account; no route takes an actor, receipt time or acknowledgement from the client.
public static class DriverReportEndpoints
{
    public static void MapDriverReports(this IEndpointRouteBuilder app)
    {
        var reports = app.MapGroup("/api/driver/journeys/{journeyId:guid}/reports")
            .RequireAuthorization(AccessPolicies.ActiveAccount);

        reports.MapPost("", Submit);
        reports.MapGet("", ListOwn);
        reports.MapGet("{reportId:guid}", ReadOwn);
    }

    // 201 for a new report, 200 for a retry of the same one with its current state.
    private static async Task<IResult> Submit(
        Guid journeyId, ReportRequest? request, HttpContext context,
        IJourneyStopReader journeys, AccessGuard guard, CurrentAccount current,
        ReportSubmitter submitter, IAccountNames names)
    {
        if (await RefuseAsync(journeyId, Permissions.DriverReportsCreate, context, journeys, guard) is { } refused)
        {
            return refused;
        }

        if (request?.ClientReportId is not { } clientReportId || clientReportId == Guid.Empty)
        {
            return Bad("clientReportId is required and must be a UUID.");
        }

        if (!ReasonCodes.IsValid(request.ReasonCode))
        {
            return Bad($"reasonCode must be one of {string.Join(", ", ReasonCodes.All)}.");
        }

        var driver = (await current.GetAsync(context.User, context.RequestAborted))!;
        var result = await submitter.SubmitAsync(context, driver.Id, journeyId, clientReportId, request.ReasonCode);
        switch (result)
        {
            case SubmitResult.Created created:
                return Results.Created(
                    $"/api/driver/journeys/{journeyId}/reports/{created.Report.Id}",
                    DriverReportDto.From(created.Report, await NamesOfAsync([created.Report], names, context)));
            case SubmitResult.Replayed replayed:
                return Results.Ok(DriverReportDto.From(replayed.Report, await NamesOfAsync([replayed.Report], names, context)));
            default:
                return Results.Problem(
                    title: "Report key already used",
                    detail: "This clientReportId belongs to a different report. Use a new one.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["code"] = "CLIENT_REPORT_ID_IN_USE" });
        }
    }

    // The driver's own reports on this journey, newest first. clientReportId finds a report
    // whose response was lost.
    private static async Task<IResult> ListOwn(
        Guid journeyId, Guid? clientReportId, int? limit, string? cursor, HttpContext context,
        IJourneyStopReader journeys, AccessGuard guard, CurrentAccount current, ReportStore store, IAccountNames names)
    {
        if (await RefuseAsync(journeyId, Permissions.DriverReportsRead, context, journeys, guard) is { } refused)
        {
            return refused;
        }

        PageCursor? after = null;
        if (cursor is not null)
        {
            if (!PageCursor.TryDecode(cursor, out var decoded))
            {
                return Bad("cursor is not a continuation token from this list.");
            }

            after = decoded;
        }

        var driver = (await current.GetAsync(context.User, context.RequestAborted))!;
        var size = PageCursor.SizeFor(limit);
        var found = await store.ListOwnAsync(journeyId, driver.Id, clientReportId, after, size, context.RequestAborted);

        var page = found.Take(size).ToList();
        var lookup = await NamesOfAsync(page, names, context);
        var next = found.Count > size ? new PageCursor(page[^1].ReceivedAt, page[^1].Id).Encode() : null;
        return Results.Ok(new DriverReportPageDto(page.Select(r => DriverReportDto.From(r, lookup)).ToList(), next));
    }

    private static async Task<IResult> ReadOwn(
        Guid journeyId, Guid reportId, HttpContext context,
        IJourneyStopReader journeys, AccessGuard guard, CurrentAccount current, ReportStore store, IAccountNames names)
    {
        if (await RefuseAsync(journeyId, Permissions.DriverReportsRead, context, journeys, guard) is { } refused)
        {
            return refused;
        }

        var driver = (await current.GetAsync(context.User, context.RequestAborted))!;
        var report = await store.FindOwnAsync(reportId, journeyId, driver.Id, context.RequestAborted);
        return report is null
            ? Results.Problem(title: "Unknown report", detail: "No report of yours has this ID.", statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(DriverReportDto.From(report, await NamesOfAsync([report], names, context)));
    }

    // The response to send when the journey is unknown or the caller may not use it; null to go on.
    // A refusal is audited by the guard with the journey as its target.
    private static async Task<IResult?> RefuseAsync(
        Guid journeyId, string permission, HttpContext context, IJourneyStopReader journeys, AccessGuard guard)
    {
        if (await journeys.FindAsync(journeyId, context.RequestAborted) is null)
        {
            return Results.Problem(
                title: "Unknown journey", detail: "No journey has this ID.", statusCode: StatusCodes.Status404NotFound);
        }

        return await guard.CheckAsync(context, permission, new AccessTarget.Journey(journeyId)) == AccessDecision.Allowed
            ? null
            : Results.Forbid();
    }

    private static Task<IReadOnlyDictionary<Guid, string>> NamesOfAsync(
        IEnumerable<DriverReport> reports, IAccountNames names, HttpContext context) =>
        names.FindAsync(
            reports.Where(r => r.AcknowledgedById is not null).Select(r => r.AcknowledgedById!.Value).Distinct().ToList(),
            context.RequestAborted);

    private static IResult Bad(string detail) =>
        Results.Problem(title: "Invalid report request", detail: detail, statusCode: StatusCodes.Status400BadRequest);
}
