using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Operations.Reports;

// Maps the driver report table in the changes schema.
public static class ReportsModel
{
    // The database, not the application, makes a retry key usable once.
    public const string ClientKeyIndex = "ux_driver_reports_client_report_id";

    public static void ApplyReportsModel(this ModelBuilder model)
    {
        model.Entity<DriverReport>(report =>
        {
            var reasons = string.Join(", ", ReasonCodes.All.Select(code => $"'{code}'"));
            report.ToTable("driver_reports", OperationsModel.Schema, table =>
            {
                table.HasCheckConstraint("ck_driver_reports_reason", $"reason_code IN ({reasons})");
                // Acknowledged by someone at some time, or by no one at no time.
                table.HasCheckConstraint(
                    "ck_driver_reports_acknowledgement", "(acknowledged_by_id IS NULL) = (acknowledged_at IS NULL)");
            });
            report.HasKey(r => r.Id);
            report.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            report.Property(r => r.JourneyId).HasColumnName("journey_id");
            report.Property(r => r.SubmittedById).HasColumnName("submitted_by_id");
            report.Property(r => r.ClientReportId).HasColumnName("client_report_id");
            report.Property(r => r.ReasonCode).HasColumnName("reason_code").HasMaxLength(32);
            report.Property(r => r.ReceivedAt).HasColumnName("received_at");
            report.Property(r => r.AcknowledgedById).HasColumnName("acknowledged_by_id");
            report.Property(r => r.AcknowledgedAt).HasColumnName("acknowledged_at");
            report.Ignore(r => r.State);

            report.HasIndex(r => r.ClientReportId).IsUnique().HasDatabaseName(ClientKeyIndex);
            // A driver's own reports on one journey, newest first.
            report.HasIndex(r => new { r.JourneyId, r.SubmittedById, r.ReceivedAt, r.Id });
            report.HasOne<ScheduledJourney>().WithMany().HasForeignKey(r => r.JourneyId).OnDelete(DeleteBehavior.Restrict);
            report.HasOne<Account>().WithMany().HasForeignKey(r => r.SubmittedById).OnDelete(DeleteBehavior.Restrict);
            report.HasOne<Account>().WithMany().HasForeignKey(r => r.AcknowledgedById).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
