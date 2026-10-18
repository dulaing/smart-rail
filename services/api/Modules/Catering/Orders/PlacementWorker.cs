using Microsoft.Extensions.Options;

namespace SmartRail.Api.Modules.Catering.Orders;

// The retry loop for orders whose outcome is unknown. It finds its work in PostgreSQL, so a restart
// loses nothing: pending links are simply picked up again.
public sealed class PlacementWorker(
    IServiceScopeFactory scopes, IOptions<PlacementOptions> options, ILogger<PlacementWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        options.Value.WorkerEnabled
            ? BackgroundLoop.RunAsync(
                scopes, TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds)),
                (services, token) => services.GetRequiredService<PlacementProcessor>().RunDueAsync(token),
                logger, stoppingToken)
            : Task.CompletedTask;
}
