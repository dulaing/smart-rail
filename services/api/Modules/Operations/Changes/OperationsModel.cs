using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Incidents;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Operations.Changes;

// The journey's change counter. Publishing or withdrawing moves it from v to v+1 in the same
// transaction as the new history row, so a stale preview can be detected.
public sealed class JourneyChangeVersion
{
    public Guid JourneyId { get; init; }
    public int Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// Maps the Operations module's tables in the changes schema.
public static class OperationsModel
{
    public const string Schema = "changes";

    public static void ApplyOperationsModel(this ModelBuilder model)
    {
        model.Entity<JourneyChangeVersion>(version =>
        {
            version.ToTable("journey_change_versions", Schema, table =>
                table.HasCheckConstraint("ck_journey_change_versions_version", "version >= 0"));
            version.HasKey(v => v.JourneyId);
            version.Property(v => v.JourneyId).HasColumnName("journey_id").ValueGeneratedNever();
            version.Property(v => v.Version).HasColumnName("version");
            version.Property(v => v.UpdatedAt).HasColumnName("updated_at");
            version.HasOne<ScheduledJourney>().WithOne().HasForeignKey<JourneyChangeVersion>(v => v.JourneyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ServiceChange>(change =>
        {
            change.ToTable("service_changes", Schema, table =>
            {
                table.HasCheckConstraint("ck_service_changes_action", "action IN ('PUBLISH', 'WITHDRAW')");
                table.HasCheckConstraint("ck_service_changes_kind", "kind IN ('DELAY', 'CANCEL', 'PLATFORM', 'EARLY_END')");
                table.HasCheckConstraint("ck_service_changes_version", "journey_version >= 1");
                // A publish carries exactly the fields of its kind; a withdrawal carries none and
                // points at the change it withdraws.
                table.HasCheckConstraint("ck_service_changes_fields", """
                    (action = 'PUBLISH' AND supersedes_change_id IS NULL AND (
                        (kind = 'DELAY' AND delay_minutes >= 0 AND new_platform IS NULL AND target_stop_id IS NULL AND final_stop_id IS NULL)
                     OR (kind = 'CANCEL' AND delay_minutes IS NULL AND new_platform IS NULL AND target_stop_id IS NULL AND final_stop_id IS NULL)
                     OR (kind = 'PLATFORM' AND delay_minutes IS NULL AND btrim(new_platform) <> '' AND target_stop_id IS NOT NULL AND final_stop_id IS NULL)
                     OR (kind = 'EARLY_END' AND delay_minutes IS NULL AND new_platform IS NULL AND target_stop_id IS NULL AND final_stop_id IS NOT NULL)))
                    OR (action = 'WITHDRAW' AND supersedes_change_id IS NOT NULL
                        AND delay_minutes IS NULL AND new_platform IS NULL AND target_stop_id IS NULL AND final_stop_id IS NULL)
                    """);
            });
            change.HasKey(c => c.Id);
            change.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            change.Property(c => c.JourneyId).HasColumnName("journey_id");
            change.Property(c => c.JourneyVersion).HasColumnName("journey_version");
            change.Property(c => c.Action).HasColumnName("action").HasMaxLength(16)
                .HasConversion(action => action.ToCode(), code => code == "PUBLISH" ? ChangeAction.Publish : ChangeAction.Withdraw);
            change.Property(c => c.Kind).HasColumnName("kind").HasMaxLength(16)
                .HasConversion(kind => kind.ToCode(), code => ParseKind(code));
            change.Property(c => c.DelayMinutes).HasColumnName("delay_minutes");
            change.Property(c => c.NewPlatform).HasColumnName("new_platform").HasMaxLength(16);
            change.Property(c => c.TargetStopId).HasColumnName("target_stop_id");
            change.Property(c => c.FinalStopId).HasColumnName("final_stop_id");
            change.Property(c => c.SupersedesChangeId).HasColumnName("supersedes_change_id");
            change.Property(c => c.IncidentId).HasColumnName("incident_id");
            change.Property(c => c.PublishedById).HasColumnName("published_by_id");
            change.Property(c => c.PublishedAt).HasColumnName("published_at");
            change.Ignore(c => c.Details);

            // The database, not the application, makes two writers at one version impossible.
            change.HasIndex(c => new { c.JourneyId, c.JourneyVersion }).IsUnique();
            change.HasOne<ScheduledJourney>().WithMany().HasForeignKey(c => c.JourneyId).OnDelete(DeleteBehavior.Restrict);
            change.HasOne<PatternStop>().WithMany().HasForeignKey(c => c.TargetStopId).OnDelete(DeleteBehavior.Restrict);
            change.HasOne<PatternStop>().WithMany().HasForeignKey(c => c.FinalStopId).OnDelete(DeleteBehavior.Restrict);
            change.HasOne<ServiceChange>().WithMany().HasForeignKey(c => c.SupersedesChangeId).OnDelete(DeleteBehavior.Restrict);
            change.HasOne<Account>().WithMany().HasForeignKey(c => c.PublishedById).OnDelete(DeleteBehavior.Restrict);
            change.HasOne<Incident>().WithMany().HasForeignKey(c => c.IncidentId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static ChangeKind ParseKind(string code) =>
        ChangeCodes.TryParseKind(code, out var kind)
            ? kind
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown change kind.");
}
