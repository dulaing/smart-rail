using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Access;

namespace SmartRail.Api.Modules.Operations.Reports;

public abstract record AcknowledgeResult
{
    // This request recorded the acknowledgement.
    public sealed record Acknowledged(DriverReport Report) : AcknowledgeResult;

    // Someone acknowledged it first, maybe this same controller earlier. Nothing was changed.
    public sealed record AlreadyAcknowledged(DriverReport Report) : AcknowledgeResult;

    public sealed record NotFound : AcknowledgeResult;
}

// Records a controller's explicit acknowledgement. The first controller wins: the update only
// matches a report that is still unacknowledged, so a second or repeated request changes nothing
// and writes no second audit row. The update and its audit row commit together or not at all.
public sealed class ReportAcknowledger(SmartRailDbContext db, IAuditTrail audit, ReportStore store, TimeProvider time)
{
    public const string AcknowledgeAction = "report.acknowledge";

    public async Task<AcknowledgeResult> AcknowledgeAsync(HttpContext context, Guid controllerId, Guid reportId)
    {
        var cancellationToken = context.RequestAborted;
        var now = PostgresTime.Round(time.GetUtcNow());

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // A second controller waits here for the first to finish, then finds the report taken.
        var won = await db.DriverReports
            .Where(r => r.Id == reportId && r.AcknowledgedAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.AcknowledgedById, controllerId)
                .SetProperty(r => r.AcknowledgedAt, now), cancellationToken);

        if (won == 1)
        {
            audit.Stage(context, controllerId, AcknowledgeAction, AuditOutcomes.Succeeded, ReportSubmitter.ReportTarget, reportId);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        var report = await store.FindAsync(reportId, cancellationToken);
        return report is null
            ? new AcknowledgeResult.NotFound()
            : won == 1
                ? new AcknowledgeResult.Acknowledged(report)
                : new AcknowledgeResult.AlreadyAcknowledged(report);
    }
}
