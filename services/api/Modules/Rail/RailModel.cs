using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Rail;

// Maps the Rail module's tables: the timetable in the network schema and simulated
// observations in the live schema.
public static class RailModel
{
    public const string Schema = "network";
    public const string LiveSchema = "live";

    public static void ApplyRailModel(this ModelBuilder model)
    {
        model.Entity<Station>(station =>
        {
            station.ToTable("stations", Schema);
            station.HasKey(s => s.Id);
            station.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            station.Property(s => s.Code).HasColumnName("code").HasMaxLength(16);
            station.Property(s => s.NameEn).HasColumnName("name_en").HasMaxLength(100);
            station.Property(s => s.NameSi).HasColumnName("name_si").HasMaxLength(100);
            station.Property(s => s.NameTa).HasColumnName("name_ta").HasMaxLength(100);
            station.HasIndex(s => s.Code).IsUnique();
        });

        model.Entity<ServicePattern>(pattern =>
        {
            pattern.ToTable("service_patterns", Schema, table =>
            {
                table.HasCheckConstraint("ck_service_patterns_source_kind",
                    "source_kind IN ('PUBLISHED', 'FICTIONAL_DEMO')");
                table.HasCheckConstraint("ck_service_patterns_published_source",
                    "source_kind <> 'PUBLISHED' OR (source_url IS NOT NULL AND source_checked_at IS NOT NULL)");
                table.HasCheckConstraint("ck_service_patterns_operating_days",
                    "operating_days_mask BETWEEN 1 AND 127");
                table.HasCheckConstraint("ck_service_patterns_effective_dates",
                    "effective_to IS NULL OR effective_to >= effective_from");
            });
            pattern.HasKey(p => p.Id);
            pattern.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
            pattern.Property(p => p.TrainNumber).HasColumnName("train_number").HasMaxLength(32);
            pattern.Property(p => p.SourceKind).HasColumnName("source_kind").HasMaxLength(16)
                .HasConversion(kind => kind.ToCode(), code => SourceKinds.FromCode(code));
            pattern.Property(p => p.SourceUrl).HasColumnName("source_url").HasMaxLength(500);
            pattern.Property(p => p.SourceCheckedAt).HasColumnName("source_checked_at");
            pattern.Property(p => p.OperatingDays).HasColumnName("operating_days_mask");
            pattern.Property(p => p.EffectiveFrom).HasColumnName("effective_from");
            pattern.Property(p => p.EffectiveTo).HasColumnName("effective_to");
            pattern.HasMany(p => p.Stops).WithOne().HasForeignKey(s => s.PatternId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PatternStop>(stop =>
        {
            stop.ToTable("pattern_stops", Schema, table =>
            {
                table.HasCheckConstraint("ck_pattern_stops_has_a_time",
                    "arrival_local IS NOT NULL OR departure_local IS NOT NULL");
                table.HasCheckConstraint("ck_pattern_stops_day_offset", "day_offset >= 0");
            });
            stop.HasKey(s => s.Id);
            stop.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            stop.Property(s => s.PatternId).HasColumnName("pattern_id");
            stop.Property(s => s.StationId).HasColumnName("station_id");
            stop.Property(s => s.Sequence).HasColumnName("stop_sequence");
            stop.Property(s => s.ArrivalLocal).HasColumnName("arrival_local");
            stop.Property(s => s.DepartureLocal).HasColumnName("departure_local");
            stop.Property(s => s.DayOffset).HasColumnName("day_offset");
            stop.Property(s => s.PlannedPlatform).HasColumnName("planned_platform").HasMaxLength(16);
            // A route may visit a station twice, so only the sequence is unique.
            stop.HasIndex(s => new { s.PatternId, s.Sequence }).IsUnique();
            stop.HasIndex(s => s.StationId);
            stop.HasOne<Station>().WithMany().HasForeignKey(s => s.StationId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ScheduledJourney>(journey =>
        {
            journey.ToTable("journeys", Schema);
            journey.HasKey(j => j.Id);
            journey.Property(j => j.Id).HasColumnName("id").ValueGeneratedNever();
            journey.Property(j => j.PatternId).HasColumnName("pattern_id");
            journey.Property(j => j.ServiceDate).HasColumnName("service_date");
            journey.Property(j => j.DemoSeedKey).HasColumnName("demo_seed_key").HasMaxLength(100);
            journey.HasIndex(j => new { j.PatternId, j.ServiceDate }).IsUnique();
            journey.HasIndex(j => j.DemoSeedKey).IsUnique();
            journey.HasOne(j => j.Pattern).WithMany().HasForeignKey(j => j.PatternId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Observation>(observation =>
        {
            observation.ToTable("observations", LiveSchema, table =>
            {
                table.HasCheckConstraint("ck_observations_progress", "progress_permille BETWEEN 0 AND 1000");
                table.HasCheckConstraint("ck_observations_delay", "reported_delay_seconds >= 0");
            });
            observation.HasKey(o => o.Id);
            observation.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();
            observation.Property(o => o.JourneyId).HasColumnName("journey_id");
            observation.Property(o => o.SourceEventId).HasColumnName("source_event_id").HasMaxLength(ObservationRules.MaxEventIdLength);
            observation.Property(o => o.FromStopId).HasColumnName("from_stop_id");
            observation.Property(o => o.ToStopId).HasColumnName("to_stop_id");
            observation.Property(o => o.ProgressPermille).HasColumnName("progress_permille");
            observation.Property(o => o.ReportedDelaySeconds).HasColumnName("reported_delay_seconds");
            observation.Property(o => o.ObservedAt).HasColumnName("observed_at");
            observation.Property(o => o.ReceivedAt).HasColumnName("received_at");
            // The database, not the application, makes a retry or a race safe.
            observation.HasIndex(o => o.SourceEventId).IsUnique();
            observation.HasIndex(o => new { o.JourneyId, o.ObservedAt });
            observation.HasOne<ScheduledJourney>().WithMany().HasForeignKey(o => o.JourneyId).OnDelete(DeleteBehavior.Restrict);
            observation.HasOne<PatternStop>().WithMany().HasForeignKey(o => o.FromStopId).OnDelete(DeleteBehavior.Restrict);
            observation.HasOne<PatternStop>().WithMany().HasForeignKey(o => o.ToStopId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
