using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Catering.Orders;
using SmartRail.Api.Modules.Catering.Outbox;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Catering;

// Maps the Catering module's tables in the catering schema.
public static class CateringModel
{
    public const string Schema = "catering";

    public static void ApplyCateringModel(this ModelBuilder model)
    {
        model.Entity<OutboxMessage>(message =>
        {
            message.ToTable("outbox_messages", Schema, table =>
            {
                table.HasCheckConstraint("ck_outbox_messages_state", "delivery_state IN ('PENDING', 'DELIVERED', 'FAILED')");
                table.HasCheckConstraint("ck_outbox_messages_attempts", "attempt_count >= 0");
            });
            message.HasKey(m => m.Id);
            message.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            message.Property(m => m.ServiceChangeId).HasColumnName("service_change_id");
            message.Property(m => m.EventId).HasColumnName("event_id").HasMaxLength(100);
            message.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb");
            message.Property(m => m.DeliveryState).HasColumnName("delivery_state").HasMaxLength(16);
            message.Property(m => m.AttemptCount).HasColumnName("attempt_count");
            message.Property(m => m.NextAttemptAt).HasColumnName("next_attempt_at");
            message.Property(m => m.CreatedAt).HasColumnName("created_at");
            message.HasIndex(m => m.EventId).IsUnique();
            message.HasIndex(m => new { m.DeliveryState, m.NextAttemptAt });
            message.HasOne<ServiceChange>().WithMany().HasForeignKey(m => m.ServiceChangeId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<OrderLink>(link =>
        {
            link.ToTable("order_links", Schema, table =>
            {
                table.HasCheckConstraint("ck_order_links_state", "placement_state IN ('PENDING', 'CONFIRMED', 'REJECTED')");
                table.HasCheckConstraint("ck_order_links_sequence", "status_sequence >= 0 AND attempt_count >= 0");
            });
            link.HasKey(l => l.Id);
            link.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();
            link.Property(l => l.AccountId).HasColumnName("account_id");
            link.Property(l => l.JourneyId).HasColumnName("journey_id");
            link.Property(l => l.PickupStopId).HasColumnName("pickup_stop_id");
            link.Property(l => l.ClientRequestKey).HasColumnName("client_request_key").HasMaxLength(100);
            link.Property(l => l.CollectionPointId).HasColumnName("collection_point_id").HasMaxLength(100);
            link.Property(l => l.RequestDigest).HasColumnName("request_digest").HasMaxLength(64);
            link.Property(l => l.RequestPayload).HasColumnName("request_payload").HasColumnType("jsonb");
            link.Property(l => l.PlacementState).HasColumnName("placement_state").HasMaxLength(16);
            link.Property(l => l.RejectionCode).HasColumnName("rejection_code").HasMaxLength(50);
            link.Property(l => l.ProviderOrderRef).HasColumnName("provider_order_ref").HasMaxLength(100);
            link.Property(l => l.ProviderStatus).HasColumnName("provider_status").HasMaxLength(16);
            link.Property(l => l.StatusSequence).HasColumnName("status_sequence");
            link.Property(l => l.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(50);
            link.Property(l => l.PickupCode).HasColumnName("pickup_code").HasMaxLength(20);
            link.Property(l => l.ItemsJson).HasColumnName("items").HasColumnType("jsonb");
            link.Property(l => l.ScheduledArrival).HasColumnName("scheduled_arrival");
            link.Property(l => l.EstimatedArrival).HasColumnName("estimated_arrival");
            link.Property(l => l.EtaEvidenceAt).HasColumnName("eta_evidence_at");
            link.Property(l => l.AttemptCount).HasColumnName("attempt_count");
            link.Property(l => l.NextAttemptAt).HasColumnName("next_attempt_at");
            link.Property(l => l.LastAttemptAt).HasColumnName("last_attempt_at");
            link.Property(l => l.LastError).HasColumnName("last_error").HasMaxLength(200);
            link.Property(l => l.CreatedAt).HasColumnName("created_at");
            link.Property(l => l.UpdatedAt).HasColumnName("updated_at");
            link.HasIndex(l => new { l.AccountId, l.ClientRequestKey }).IsUnique();
            link.HasIndex(l => l.ProviderOrderRef).IsUnique();
            link.HasIndex(l => new { l.PlacementState, l.NextAttemptAt });
            link.HasOne<Account>().WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
            link.HasOne<ScheduledJourney>().WithMany().HasForeignKey(l => l.JourneyId).OnDelete(DeleteBehavior.Restrict);
            link.HasOne<PatternStop>().WithMany().HasForeignKey(l => l.PickupStopId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
