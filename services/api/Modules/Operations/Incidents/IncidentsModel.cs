using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Operations.Reports;

namespace SmartRail.Api.Modules.Operations.Incidents;

// Maps the incident table in the changes schema.
public static class IncidentsModel
{
    // The database, not the application, lets a report start only one incident.
    public const string SourceReportIndex = "ux_incidents_source_report_id";

    public static void ApplyIncidentsModel(this ModelBuilder model)
    {
        model.Entity<Incident>(incident =>
        {
            var states = string.Join(", ", IncidentStates.All.Select(state => $"'{state}'"));
            var reasons = string.Join(", ", ReasonCodes.All.Select(code => $"'{code}'"));
            incident.ToTable("incidents", OperationsModel.Schema, table =>
            {
                table.HasCheckConstraint("ck_incidents_status", $"status IN ({states})");
                table.HasCheckConstraint("ck_incidents_reason", $"reason_code IN ({reasons})");
                // Resolved exactly when there is a resolution time.
                table.HasCheckConstraint("ck_incidents_resolution", "(status = 'RESOLVED') = (resolved_at IS NOT NULL)");
            });
            incident.HasKey(i => i.Id);
            incident.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
            incident.Property(i => i.SourceReportId).HasColumnName("source_report_id");
            incident.Property(i => i.Status).HasColumnName("status").HasMaxLength(16);
            incident.Property(i => i.ReasonCode).HasColumnName("reason_code").HasMaxLength(32);
            incident.Property(i => i.OpenedById).HasColumnName("opened_by_id");
            incident.Property(i => i.OpenedAt).HasColumnName("opened_at");
            incident.Property(i => i.ResolvedAt).HasColumnName("resolved_at");

            incident.HasIndex(i => i.SourceReportId).IsUnique().HasDatabaseName(SourceReportIndex);
            // The list, newest first.
            incident.HasIndex(i => new { i.OpenedAt, i.Id });
            incident.HasOne<DriverReport>().WithMany().HasForeignKey(i => i.SourceReportId).OnDelete(DeleteBehavior.Restrict);
            incident.HasOne<Account>().WithMany().HasForeignKey(i => i.OpenedById).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
