using System.Diagnostics.Metrics;

namespace SmartRail.Api.Modules.Catering.Callbacks;

// The counters operators watch on the catering boundary. They are exported with the rest of the API's
// telemetry; nothing here carries an order, a pickup code, a signature or a payload.
public static class CateringMetrics
{
    public const string MeterName = "SmartRail.Catering";

    private static readonly Meter Meter = new(MeterName);

    // A callback refused before it was parsed: bad or missing signature, stale timestamp, no secret.
    public static readonly Counter<long> CallbacksRejected =
        Meter.CreateCounter<long>("catering.callbacks.rejected", description: "Status callbacks refused as unauthenticated.");

    // A callback that passed authentication, by what happened to it.
    public static readonly Counter<long> CallbacksReceived =
        Meter.CreateCounter<long>("catering.callbacks.received", description: "Authenticated status callbacks, by result.");
}
