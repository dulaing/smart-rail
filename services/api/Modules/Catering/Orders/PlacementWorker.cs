using Microsoft.Extensions.Options;

namespace SmartRail.Api.Modules.Catering.Orders;

// The retry loop for orders whose outcome is unknown. It lives inside the API process and finds
// its work in PostgreSQL, so a restart loses nothing: pending links are simply picked up again.
public sealed class PlacementWorker(
    IServiceScopeFactory scopes, IOptions<PlacementOptions> options, ILogger<PlacementWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds)));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PlacementProcessor>().RunDueAsync(stoppingToken);
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // One bad pass must not stop the loop; the next tick tries again.
                logger.LogError(exception, "Placement retry pass failed.");
                try
                {
                    await timer.WaitForNextTickAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
