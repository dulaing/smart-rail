using System.Text.Json;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Catering.Provider;

namespace SmartRail.Api.Modules.Catering.Outbox;

public static class DeliveryStates
{
    public const string Pending = "PENDING";
    public const string Delivered = "DELIVERED";

    // A permanent refusal (bad credential, a contract mismatch). The message is kept, not dropped,
    // and retried slowly so that fixing the cause lets it through without anyone touching the row.
    public const string Failed = "FAILED";
}

public static class PayloadVersions
{
    // The first format: eligibility only, in snake_case, with no pickup times. Rows written before
    // pickup times existed are still this version until they are upgraded.
    public const int EligibilityOnly = 1;

    // The RailBites JourneyEvent contract: eligibility snapshot plus every stop's pickup time.
    public const int WithPickupTimes = 2;
}

// One message waiting to be delivered to RailBites, in catering.outbox_messages. Writing it with
// the change is what makes the handoff safe when RailBites is down. The dispatcher retries from
// attempt_count and next_attempt_at until RailBites confirms it has committed the message.
public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public Guid ServiceChangeId { get; init; }
    public required string EventId { get; init; }
    public required string Payload { get; set; }
    public int PayloadVersion { get; set; } = PayloadVersions.WithPickupTimes;
    public required string DeliveryState { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}

// Builds the exact body RailBites receives. It is stored once and sent unchanged on every retry.
public static class OutboxPayloads
{
    public static string EventIdFor(Guid serviceChangeId) => $"train-change:{serviceChangeId}";

    public static string Build(TrainChangeMessage message)
    {
        var payload = new
        {
            eventId = EventIdFor(message.ServiceChangeId),
            eventType = message.Action == "WITHDRAW" ? "WITHDRAWAL" : message.ChangeKind,
            journey = new
            {
                journeyId = message.JourneyId,
                trainNumber = message.TrainNumber,
                serviceDate = message.ServiceDate,
                version = message.JourneyVersion,
                orderClosed = message.OrderClosed,
                blockedPickupStopIds = message.BlockedPickupStopIds,
            },
            stops = message.Stops.Select(s => new
            {
                stopId = s.StopId,
                stationId = s.StationId,
                scheduledArrival = s.ScheduledArrival,
                estimatedArrival = s.EstimatedArrival,
                etaEvidenceAt = s.EtaEvidenceAt,
            }),
            calculatedAt = message.CalculatedAt,
            occurredAt = message.CalculatedAt,
        };
        return JsonSerializer.Serialize(payload, ProviderJson.Options);
    }
}

public sealed class CateringOutbox(SmartRailDbContext db, TimeProvider time) : ICateringOutbox
{
    public void Enqueue(TrainChangeMessage message)
    {
        var now = time.GetUtcNow();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            ServiceChangeId = message.ServiceChangeId,
            EventId = OutboxPayloads.EventIdFor(message.ServiceChangeId),
            Payload = OutboxPayloads.Build(message),
            PayloadVersion = PayloadVersions.WithPickupTimes,
            DeliveryState = DeliveryStates.Pending,
            AttemptCount = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });
    }
}
