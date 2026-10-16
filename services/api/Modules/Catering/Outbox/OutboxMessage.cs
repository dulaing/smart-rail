using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Operations.Changes;

namespace SmartRail.Api.Modules.Catering.Outbox;

public static class DeliveryStates
{
    public const string Pending = "PENDING";
    public const string Delivered = "DELIVERED";
    public const string Failed = "FAILED";
}

// One message waiting to be delivered to RailBites, in catering.outbox_messages. Writing it with
// the change is what makes the handoff safe when RailBites is down. A later dispatcher retries
// from attempt_count and next_attempt_at.
public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public Guid ServiceChangeId { get; init; }
    public required string EventId { get; init; }
    public required string Payload { get; init; }
    public required string DeliveryState { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class CateringOutbox(SmartRailDbContext db, TimeProvider time) : ICateringOutbox
{
    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public void Enqueue(TrainChangeMessage message)
    {
        var now = time.GetUtcNow();
        var eventId = $"train-change:{message.ServiceChangeId}";
        var payload = new
        {
            event_id = eventId,
            journey_id = message.JourneyId,
            journey_version = message.JourneyVersion,
            change_kind = message.ChangeKind,
            action = message.Action,
            confirmed_delay_minutes = message.ConfirmedDelayMinutes,
            order_closed = message.OrderClosed,
            blocked_pickup_stop_ids = message.BlockedPickupStopIds,
        };

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            ServiceChangeId = message.ServiceChangeId,
            EventId = eventId,
            Payload = JsonSerializer.Serialize(payload, PayloadOptions),
            DeliveryState = DeliveryStates.Pending,
            AttemptCount = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });
    }
}

// Maps the Catering module's tables in the catering schema. Only the outbox exists so far.
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
    }
}
