using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SmartRail.Api.Modules.Catering.Callbacks;

// The counters and spans operators watch on the catering boundary. They are exported with the rest of
// the API's telemetry; nothing here carries an order, a pickup code, a signature or a payload.
public static class CateringMetrics
{
    public const string MeterName = "SmartRail.Catering";
    public const string ActivitySourceName = "SmartRail.Catering";

    private static readonly Meter Meter = new(MeterName);

    public static readonly ActivitySource Source = new(ActivitySourceName);

    // A callback refused before it was parsed: bad or missing signature, stale timestamp, no secret.
    public static readonly Counter<long> CallbacksRejected =
        Meter.CreateCounter<long>("catering.callbacks.rejected", description: "Status callbacks refused as unauthenticated, by reason.");

    // A callback that passed authentication, by what happened to it.
    public static readonly Counter<long> CallbacksReceived =
        Meter.CreateCounter<long>("catering.callbacks.received", description: "Authenticated status callbacks, by result.");

    // One try at sending a queued train change to Grilled. result: delivered, retry (will be sent
    // again) or failed (refused for good, kept and retried slowly).
    public static readonly Counter<long> OutboxAttempts =
        Meter.CreateCounter<long>("catering.outbox.attempts", description: "Train-change delivery attempts, by result.");

    // One try at placing, or finding out about, a pending order. result: confirmed, refused, retry or reconciled.
    public static readonly Counter<long> PlacementAttempts =
        Meter.CreateCounter<long>("catering.placement.attempts", description: "Order placement attempts, by result.");
}
