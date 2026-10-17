using SmartRail.Api.Modules.Access;

namespace SmartRail.Api.Modules.Operations.Reports;

// Control's routes for driver reports: the inbox, one report, and the explicit acknowledgement.
// They need the network-wide incidents.manage permission, rechecked from the database on every
// request. Acknowledging is its own action: it never opens an incident or publishes a change,
// and nothing else acknowledges a report.
public static class ControlReportEndpoints
{
    public static void MapControlReports(this IEndpointRouteBuilder app)
    {
        var reports = app.MapGroup("/api/operations/reports")
            .RequireAuthorization(AccessPolicies.For(Permissions.IncidentsManage));

        reports.MapGet("", List);
        reports.MapGet("{reportId:guid}", Read);
        reports.MapPost("{reportId:guid}/acknowledge", Acknowledge);
    }

    // Newest first. acknowledged=false is the work still waiting for a controller.
    private static async Task<IResult> List(
        Guid? journeyId, bool? acknowledged, int? limit, string? cursor, HttpContext context,
        ReportStore store, IAccountNames names)
    {
        PageCursor? after = null;
        if (cursor is not null)
        {
            if (!PageCursor.TryDecode(cursor, out var decoded))
            {
                return Results.Problem(
                    title: "Invalid report request",
                    detail: "cursor is not a continuation token from this list.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            after = decoded;
        }

        var size = PageCursor.SizeFor(limit);
        var found = await store.ListInboxAsync(journeyId, acknowledged, after, size, context.RequestAborted);

        var page = found.Take(size).ToList();
        var lookup = await NamesOfAsync(page, names, context);
        var next = found.Count > size ? new PageCursor(page[^1].ReceivedAt, page[^1].Id).Encode() : null;
        return Results.Ok(new ControlReportPageDto(page.Select(r => ControlReportDto.From(r, lookup)).ToList(), next));
    }

    private static async Task<IResult> Read(Guid reportId, HttpContext context, ReportStore store, IAccountNames names)
    {
        var report = await store.FindAsync(reportId, context.RequestAborted);
        return report is null
            ? UnknownReport()
            : Results.Ok(ControlReportDto.From(report, await NamesOfAsync([report], names, context)));
    }

    // 200 whether this request acknowledged the report or it was already acknowledged: either
    // way the answer is the winning controller's name and time, which a repeat never overwrites.
    private static async Task<IResult> Acknowledge(
        Guid reportId, HttpContext context, CurrentAccount current, ReportAcknowledger acknowledger, IAccountNames names)
    {
        var controller = (await current.GetAsync(context.User, context.RequestAborted))!;
        var report = await acknowledger.AcknowledgeAsync(context, controller.Id, reportId) switch
        {
            AcknowledgeResult.Acknowledged done => done.Report,
            AcknowledgeResult.AlreadyAcknowledged already => already.Report,
            _ => null,
        };

        return report is null
            ? UnknownReport()
            : Results.Ok(ControlReportDto.From(report, await NamesOfAsync([report], names, context)));
    }

    private static Task<IReadOnlyDictionary<Guid, string>> NamesOfAsync(
        IEnumerable<DriverReport> reports, IAccountNames names, HttpContext context) =>
        names.FindAsync(
            reports.SelectMany(r => new[] { r.SubmittedById, r.AcknowledgedById ?? Guid.Empty })
                .Where(id => id != Guid.Empty).Distinct().ToList(),
            context.RequestAborted);

    private static IResult UnknownReport() =>
        Results.Problem(title: "Unknown report", detail: "No report has this ID.", statusCode: StatusCodes.Status404NotFound);
}
