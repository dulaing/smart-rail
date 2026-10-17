using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Reports;

namespace SmartRail.Api.Modules.Operations.Incidents;

// Control's routes for incidents. Opening and resolving need incidents.manage; reading needs
// incidents.read, which controllers and auditors hold; the incident's audit trail needs
// audit.read, which only auditors hold. Each is rechecked from the database on every request.
public static class IncidentEndpoints
{
    public const int DefaultAuditLimit = 100;
    public const int MaxAuditLimit = 200;

    public static void MapIncidents(this IEndpointRouteBuilder app)
    {
        var incidents = app.MapGroup("/api/operations/incidents");

        incidents.MapPost("", Open).RequireAuthorization(AccessPolicies.For(Permissions.IncidentsManage));
        incidents.MapPost("{incidentId:guid}/resolve", Resolve).RequireAuthorization(AccessPolicies.For(Permissions.IncidentsManage));
        incidents.MapGet("", List).RequireAuthorization(AccessPolicies.For(Permissions.IncidentsRead));
        incidents.MapGet("{incidentId:guid}", Read).RequireAuthorization(AccessPolicies.For(Permissions.IncidentsRead));
        incidents.MapGet("{incidentId:guid}/audit", ReadAudit).RequireAuthorization(AccessPolicies.For(Permissions.AuditRead));
    }

    // 201 for a new incident, 200 when the report had already started one.
    private static async Task<IResult> Open(
        IncidentRequest? request, HttpContext context, CurrentAccount current, IncidentOpener opener, IAccountNames names)
    {
        if (request is null || (request.SourceReportId is null) == (request.ReasonCode is null))
        {
            return Bad("Send either sourceReportId or reasonCode.");
        }

        var controller = (await current.GetAsync(context.User, context.RequestAborted))!;
        OpenResult result;
        if (request.SourceReportId is { } reportId)
        {
            if (reportId == Guid.Empty)
            {
                return Bad("sourceReportId must be a report ID.");
            }

            result = await opener.OpenFromReportAsync(context, controller.Id, reportId);
        }
        else if (ReasonCodes.IsValid(request.ReasonCode))
        {
            result = await opener.OpenStandaloneAsync(context, controller.Id, request.ReasonCode);
        }
        else
        {
            return Bad($"reasonCode must be one of {string.Join(", ", ReasonCodes.All)}.");
        }

        switch (result)
        {
            case OpenResult.Opened opened:
                return Results.Created(
                    $"/api/operations/incidents/{opened.Incident.Id}", await ToDtoAsync(opened.Incident, names, context));
            case OpenResult.AlreadyOpened existing:
                return Results.Ok(await ToDtoAsync(existing.Incident, names, context));
            default:
                return Results.Problem(
                    title: "Unknown report", detail: "No report has this ID.", statusCode: StatusCodes.Status404NotFound);
        }
    }

    // 200 whether this request resolved it or it was already resolved: the first resolution time stays.
    private static async Task<IResult> Resolve(
        Guid incidentId, HttpContext context, CurrentAccount current, IncidentResolver resolver, IAccountNames names)
    {
        var controller = (await current.GetAsync(context.User, context.RequestAborted))!;
        var incident = await resolver.ResolveAsync(context, controller.Id, incidentId) switch
        {
            ResolveResult.Resolved done => done.Incident,
            ResolveResult.AlreadyResolved already => already.Incident,
            _ => null,
        };

        return incident is null ? UnknownIncident() : Results.Ok(await ToDtoAsync(incident, names, context));
    }

    // Newest first. state is OPEN or RESOLVED, in any letter case.
    private static async Task<IResult> List(
        string? state, int? limit, string? cursor, HttpContext context, IncidentStore store, IAccountNames names)
    {
        string? wanted = null;
        if (state is not null && (wanted = IncidentStates.Parse(state)) is null)
        {
            return Bad($"state must be one of {string.Join(", ", IncidentStates.All)}.");
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

        var size = PageCursor.SizeFor(limit);
        var found = await store.ListAsync(wanted, after, size, context.RequestAborted);

        var page = found.Take(size).ToList();
        var lookup = await names.FindAsync(page.Select(i => i.OpenedById).Distinct().ToList(), context.RequestAborted);
        var next = found.Count > size ? new PageCursor(page[^1].OpenedAt, page[^1].Id).Encode() : null;
        return Results.Ok(new IncidentPageDto(page.Select(i => IncidentDto.From(i, lookup)).ToList(), next));
    }

    private static async Task<IResult> Read(Guid incidentId, HttpContext context, IncidentStore store, IAccountNames names)
    {
        var incident = await store.FindAsync(incidentId, context.RequestAborted);
        return incident is null ? UnknownIncident() : Results.Ok(await ToDtoAsync(incident, names, context));
    }

    // Who did what to this incident and when, oldest first, at most limit entries.
    private static async Task<IResult> ReadAudit(
        Guid incidentId, int? limit, HttpContext context, IncidentStore store, IAuditTrail audit)
    {
        if (await store.FindAsync(incidentId, context.RequestAborted) is null)
        {
            return UnknownIncident();
        }

        var take = Math.Clamp(limit ?? DefaultAuditLimit, 1, MaxAuditLimit);
        var entries = await audit.ReadForTargetAsync(IncidentOpener.IncidentTarget, incidentId, take, context.RequestAborted);
        return Results.Ok(new IncidentAuditDto(incidentId, entries));
    }

    private static async Task<IncidentDto> ToDtoAsync(Incident incident, IAccountNames names, HttpContext context) =>
        IncidentDto.From(incident, await names.FindAsync([incident.OpenedById], context.RequestAborted));

    private static IResult Bad(string detail) =>
        Results.Problem(title: "Invalid incident request", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static IResult UnknownIncident() =>
        Results.Problem(title: "Unknown incident", detail: "No incident has this ID.", statusCode: StatusCodes.Status404NotFound);
}
