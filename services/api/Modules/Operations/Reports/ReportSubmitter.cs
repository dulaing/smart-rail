using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Access;

namespace SmartRail.Api.Modules.Operations.Reports;

public abstract record SubmitResult
{
    // A new report was stored with its audit row.
    public sealed record Created(DriverReport Report) : SubmitResult;

    // The same driver sent the same report again; this is the report stored the first time.
    public sealed record Replayed(DriverReport Report) : SubmitResult;

    // The key already belongs to a different report. Nothing about that report is returned.
    public sealed record KeyInUse : SubmitResult;
}

// Stores a driver's report once. The report and its successful audit row are saved together, so
// either both exist or neither does, and the retry key is made unique by the database.
public sealed class ReportSubmitter(SmartRailDbContext db, IAuditTrail audit, ReportStore store, TimeProvider time)
{
    public const string SubmitAction = "report.submit";
    public const string ReportTarget = "REPORT";

    public async Task<SubmitResult> SubmitAsync(
        HttpContext context, Guid driverId, Guid journeyId, Guid clientReportId, string reasonCode)
    {
        var cancellationToken = context.RequestAborted;

        if (await store.FindByClientIdAsync(clientReportId, cancellationToken) is { } existing)
        {
            return Classify(existing, driverId, journeyId, reasonCode);
        }

        var report = new DriverReport
        {
            Id = Guid.NewGuid(),
            JourneyId = journeyId,
            SubmittedById = driverId,
            ClientReportId = clientReportId,
            ReasonCode = reasonCode,
            ReceivedAt = PostgresTime.Round(time.GetUtcNow()),
        };
        db.DriverReports.Add(report);
        audit.Stage(context, driverId, SubmitAction, AuditOutcomes.Succeeded, ReportTarget, report.Id);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new SubmitResult.Created(report);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ReportsModel.ClientKeyIndex,
        })
        {
            // A concurrent submission with this key committed first. Neither the report nor its
            // audit row was stored here, so this request becomes a replay of the winner.
            db.ChangeTracker.Clear();
            var winner = await store.FindByClientIdAsync(clientReportId, cancellationToken)
                ?? throw new InvalidOperationException("A report key conflict left no report.");
            return Classify(winner, driverId, journeyId, reasonCode);
        }
    }

    private static SubmitResult Classify(DriverReport existing, Guid driverId, Guid journeyId, string reasonCode) =>
        existing.SubmittedById == driverId && existing.JourneyId == journeyId && existing.ReasonCode == reasonCode
            ? new SubmitResult.Replayed(existing)
            : new SubmitResult.KeyInUse();
}
