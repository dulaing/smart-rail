using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Catering.Outbox;

// Rebuilds messages that were queued before pickup times existed. Such a message holds only the
// eligibility, so delivering it as it is would leave RailBites with the old pickup times. It is
// never marked delivered by this: it is rewritten in the new format under the same event ID and
// then sent like any other. The eligibility is exactly what it was, folded from the change history
// up to the message's own journey version; the pickup times are calculated now from that state.
public sealed class OutboxUpgrader(
    SmartRailDbContext db,
    IJourneyEligibilityReader eligibility,
    IPickupReader pickups,
    TimeProvider time,
    ILogger<OutboxUpgrader> logger)
{
    public const string Unreadable = "OLD_FORMAT_UNREADABLE";

    // Every undelivered message still in the old format. Safe to run again.
    public async Task<int> UpgradeAllAsync(CancellationToken cancellationToken)
    {
        var ids = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.DeliveryState != DeliveryStates.Delivered && m.PayloadVersion < PayloadVersions.WithPickupTimes)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        var upgraded = 0;
        foreach (var id in ids)
        {
            var message = await db.OutboxMessages.SingleAsync(m => m.Id == id, cancellationToken);
            if (await UpgradeAsync(message, cancellationToken))
            {
                upgraded++;
            }
        }

        return upgraded;
    }

    // True when the message is now in the current format. When the old message cannot be rebuilt it
    // is kept as FAILED with the reason, so it is visible and nothing pretends it was sent.
    public async Task<bool> UpgradeAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.PayloadVersion >= PayloadVersions.WithPickupTimes)
        {
            return true;
        }

        var now = time.GetUtcNow();
        var rebuilt = await TryRebuildAsync(message, now, cancellationToken);
        if (rebuilt is null)
        {
            logger.LogError("Outbox message {EventId} is in the old format and cannot be rebuilt.", message.EventId);
            message.DeliveryState = DeliveryStates.Failed;
            message.LastError = Unreadable;
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }

        message.Payload = rebuilt;
        message.PayloadVersion = PayloadVersions.WithPickupTimes;
        message.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<string?> TryRebuildAsync(OutboxMessage message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Guid journeyId;
        int version;
        string kind;
        string action;
        try
        {
            using var document = JsonDocument.Parse(message.Payload);
            var root = document.RootElement;
            journeyId = root.GetProperty("journey_id").GetGuid();
            version = root.GetProperty("journey_version").GetInt32();
            kind = root.GetProperty("change_kind").GetString()!;
            action = root.GetProperty("action").GetString()!;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }

        var rules = await eligibility.ReadAtVersionAsync(journeyId, version, cancellationToken);
        var times = await pickups.ReadAsync(journeyId, now, rules?.Overrides, cancellationToken);
        if (rules is null || times is null || rules.Version != version)
        {
            return null;
        }

        return OutboxPayloads.Build(new TrainChangeMessage(
            message.ServiceChangeId,
            journeyId,
            times.TrainNumber,
            times.ServiceDate,
            version,
            kind,
            action,
            rules.OrderClosed,
            rules.BlockedPickupStopIds,
            now,
            times.Stops.Where(s => s.ScheduledArrival is not null)
                .Select(s => new TrainChangeStop(s.StopId, s.StationId, s.ScheduledArrival!.Value, s.EstimatedArrival, s.EtaEvidenceAt))
                .ToList()));
    }
}
