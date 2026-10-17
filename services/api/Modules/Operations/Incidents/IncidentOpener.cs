using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Reports;

namespace SmartRail.Api.Modules.Operations.Incidents;

public abstract record OpenResult
{
    // A new incident was stored with its audit row.
    public sealed record Opened(Incident Incident) : OpenResult;

    // The report already started an incident, open or since resolved. Nothing was written.
    public sealed record AlreadyOpened(Incident Incident) : OpenResult;

    public sealed record ReportNotFound : OpenResult;
}

// Opens an incident, from a report or on its own. The incident and its audit row are saved
// together, and the database lets a report start only one. Opening one never acknowledges the
// report it came from.
public sealed class IncidentOpener(
    SmartRailDbContext db, IAuditTrail audit, IncidentStore incidents, ReportStore reports, TimeProvider time)
{
    public const string OpenAction = "incident.open";
    public const string IncidentTarget = "INCIDENT";

    public async Task<OpenResult> OpenFromReportAsync(HttpContext context, Guid controllerId, Guid reportId)
    {
        var cancellationToken = context.RequestAborted;

        if (await incidents.FindBySourceAsync(reportId, cancellationToken) is { } existing)
        {
            return new OpenResult.AlreadyOpened(existing);
        }

        if (await reports.FindAsync(reportId, cancellationToken) is not { } report)
        {
            return new OpenResult.ReportNotFound();
        }

        return await StoreAsync(context, controllerId, report.ReasonCode, reportId);
    }

    public Task<OpenResult> OpenStandaloneAsync(HttpContext context, Guid controllerId, string reasonCode) =>
        StoreAsync(context, controllerId, reasonCode, sourceReportId: null);

    private async Task<OpenResult> StoreAsync(HttpContext context, Guid controllerId, string reasonCode, Guid? sourceReportId)
    {
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            SourceReportId = sourceReportId,
            ReasonCode = reasonCode,
            OpenedById = controllerId,
            OpenedAt = PostgresTime.Round(time.GetUtcNow()),
        };
        db.Incidents.Add(incident);
        audit.Stage(context, controllerId, OpenAction, AuditOutcomes.Succeeded, IncidentTarget, incident.Id);

        try
        {
            await db.SaveChangesAsync(context.RequestAborted);
            return new OpenResult.Opened(incident);
        }
        catch (DbUpdateException exception) when (sourceReportId is { } reportId && exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: IncidentsModel.SourceReportIndex,
        })
        {
            // A concurrent request opened it first. Neither the incident nor its audit row was
            // stored here, so this request returns the winner.
            db.ChangeTracker.Clear();
            var winner = await incidents.FindBySourceAsync(reportId, context.RequestAborted)
                ?? throw new InvalidOperationException("An incident key conflict left no incident.");
            return new OpenResult.AlreadyOpened(winner);
        }
    }
}
