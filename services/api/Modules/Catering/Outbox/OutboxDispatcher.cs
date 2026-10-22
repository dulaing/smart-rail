using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Catering.Callbacks;
using SmartRail.Api.Modules.Catering.Provider;

namespace SmartRail.Api.Modules.Catering.Outbox;

public sealed class OutboxOptions
{
    public const string Section = "Catering:Outbox";

    public int PollSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 20;

    // Retries after a timeout or 5xx wait 2, 4, 8 ... seconds, never more than this.
    public int MaxBackoffSeconds { get; set; } = 300;

    // A permanent refusal is retried this slowly, so a corrected credential or contract is picked up.
    public int FailedRetrySeconds { get; set; } = 600;

    public bool WorkerEnabled { get; set; } = true;
}

// Sends queued train-change messages to Grilled, oldest first, and records a message as delivered
// only when Grilled says it has committed it. Everything else leaves it to be sent again with the
// same event ID and the same body, so a crash on either side between commit and acknowledgement
// just causes a repeat that Grilled recognises. The queue lives in PostgreSQL, so a restart of the
// API or of Grilled loses nothing.
public sealed class OutboxDispatcher(
    SmartRailDbContext db,
    IGrilledClient provider,
    OutboxUpgrader upgrader,
    TimeProvider time,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger)
{
    // Messages that are due, oldest first. Returns how many were tried.
    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.DeliveryState != DeliveryStates.Delivered && m.NextAttemptAt <= now)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .Select(m => m.Id)
            .Take(options.Value.BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var id in due)
        {
            await DeliverAsync(id, cancellationToken);
        }

        return due.Count;
    }

    public async Task DeliverAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await db.OutboxMessages.SingleAsync(m => m.Id == messageId, cancellationToken);
        if (message.DeliveryState == DeliveryStates.Delivered)
        {
            return;
        }

        // A message from before pickup times existed is rebuilt first, never sent as it is.
        if (!await upgrader.UpgradeAsync(message, cancellationToken))
        {
            await ScheduleAsync(message, options.Value.FailedRetrySeconds, DeliveryStates.Failed, message.LastError, cancellationToken);
            return;
        }

        var now = time.GetUtcNow();
        using var activity = CateringMetrics.Source.StartActivity("catering.outbox.deliver");
        activity?.SetTag("catering.event_id", message.EventId);
        activity?.SetTag("catering.attempt", message.AttemptCount + 1);
        var response = await provider.DeliverEventAsync(message.Payload, cancellationToken);
        activity?.SetTag("catering.result", response.Outcome.ToString());
        switch (response.Outcome)
        {
            case ProviderOutcome.Success:
                message.DeliveryState = DeliveryStates.Delivered;
                message.DeliveredAt = now;
                message.LastError = null;
                message.AttemptCount++;
                await db.SaveChangesAsync(cancellationToken);
                CateringMetrics.OutboxAttempts.Add(1, new KeyValuePair<string, object?>("result", "delivered"));
                break;

            case ProviderOutcome.Refused:
                // The provider understood and said no (a malformed body, an event id reused with
                // other content). Retrying unchanged cannot help, but dropping it would lose the change.
                logger.LogError("Grilled refused train-change {EventId} with {Code}.", message.EventId, response.Error!.Code);
                await ScheduleAsync(message, options.Value.FailedRetrySeconds, DeliveryStates.Failed, response.Error.Code, cancellationToken);
                CateringMetrics.OutboxAttempts.Add(1, new KeyValuePair<string, object?>("result", "failed"));
                break;

            default:
                // Timeout, connection failure, 5xx, or a wrong credential (401), which the client
                // reports as unknown: the change was not acknowledged, so it is sent again.
                var permanent = response.HttpStatus == StatusCodes.Status401Unauthorized;
                var seconds = permanent ? options.Value.FailedRetrySeconds : Backoff(message.AttemptCount + 1);
                await ScheduleAsync(message, seconds, permanent ? DeliveryStates.Failed : DeliveryStates.Pending, response.Error?.Code, cancellationToken);
                CateringMetrics.OutboxAttempts.Add(1, new KeyValuePair<string, object?>("result", permanent ? "failed" : "retry"));
                break;
        }
    }

    private async Task ScheduleAsync(OutboxMessage message, int seconds, string state, string? error, CancellationToken cancellationToken)
    {
        message.DeliveryState = state;
        message.AttemptCount++;
        message.NextAttemptAt = time.GetUtcNow().AddSeconds(seconds);
        message.LastError = error is null ? null : error[..Math.Min(error.Length, 200)];
        await db.SaveChangesAsync(cancellationToken);
    }

    private int Backoff(int attempt) =>
        (int)Math.Min(options.Value.MaxBackoffSeconds, Math.Pow(2, Math.Min(attempt, 16)));
}
