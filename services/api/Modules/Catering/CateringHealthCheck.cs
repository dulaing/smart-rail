using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Catering.Orders;
using SmartRail.Api.Modules.Catering.Outbox;
using SmartRail.Api.Modules.Catering.Provider;

namespace SmartRail.Api.Modules.Catering;

// How healthy the food-ordering side is, for /health/ready. It is never Unhealthy: train information
// does not depend on the food provider, so a provider outage or a backlog is reported as Degraded,
// with the numbers an operator needs (how many changes and orders are waiting, and for how long).
// It carries counts and ages only, never an order, a code or a payload.
public sealed class CateringHealthCheck(
    SmartRailDbContext db, IRailBitesClient provider, IConfiguration configuration, TimeProvider time) : IHealthCheck
{
    // A change or order waiting longer than this means retries are not catching up.
    private const int DefaultBacklogSeconds = 120;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await CheckAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The database is unavailable or not yet prepared. Say so without the exception's text.
            return HealthCheckResult.Degraded("Food ordering status could not be read.");
        }
    }

    private async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var limit = TimeSpan.FromSeconds(configuration.GetValue("Catering:Health:BacklogDegradedSeconds", DefaultBacklogSeconds));
        var problems = new List<string>();

        var readiness = await provider.CheckReadyAsync(cancellationToken);
        if (readiness != ProviderReadiness.Ready)
        {
            problems.Add(readiness switch
            {
                ProviderReadiness.NotConfigured => "RailBites is not configured",
                ProviderReadiness.NotReady => "RailBites reports it is not ready",
                _ => "RailBites is unreachable",
            });
        }

        var waiting = db.OutboxMessages.AsNoTracking().Where(m => m.DeliveryState == DeliveryStates.Pending);
        var outboxPending = await waiting.CountAsync(cancellationToken);
        var outboxFailed = await db.OutboxMessages.CountAsync(m => m.DeliveryState == DeliveryStates.Failed, cancellationToken);
        var oldestChange = await waiting.MinAsync(m => (DateTimeOffset?)m.CreatedAt, cancellationToken);
        var changeAge = oldestChange is null ? TimeSpan.Zero : now - oldestChange.Value;

        var pendingOrders = db.OrderLinks.AsNoTracking().Where(l => l.PlacementState == PlacementStates.Pending);
        var ordersPending = await pendingOrders.CountAsync(cancellationToken);
        var oldestOrder = await pendingOrders.MinAsync(l => (DateTimeOffset?)l.CreatedAt, cancellationToken);
        var orderAge = oldestOrder is null ? TimeSpan.Zero : now - oldestOrder.Value;

        if (changeAge > limit)
        {
            problems.Add($"train changes have been waiting for {(int)changeAge.TotalSeconds} seconds");
        }

        if (outboxFailed > 0)
        {
            problems.Add($"{outboxFailed} train change(s) were refused and need correction");
        }

        if (orderAge > limit)
        {
            problems.Add($"an order has been waiting for {(int)orderAge.TotalSeconds} seconds");
        }

        var data = new Dictionary<string, object>
        {
            ["provider"] = readiness.ToString(),
            ["outboxPending"] = outboxPending,
            ["outboxFailed"] = outboxFailed,
            ["oldestOutboxSeconds"] = (int)changeAge.TotalSeconds,
            ["ordersPending"] = ordersPending,
            ["oldestPendingOrderSeconds"] = (int)orderAge.TotalSeconds,
        };

        return problems.Count == 0
            ? HealthCheckResult.Healthy("Food ordering is up to date.", data)
            : HealthCheckResult.Degraded(string.Join("; ", problems), data: data);
    }
}
