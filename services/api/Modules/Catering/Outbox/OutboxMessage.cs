using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;

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
