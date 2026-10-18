namespace SmartRail.Api.Modules.Catering;

// A hosted polling loop that lives inside the API process. Its work is found in PostgreSQL on each
// pass, so restarting the process loses nothing. One failed pass is logged and the loop carries on.
public static class BackgroundLoop
{
    public static async Task RunAsync(
        IServiceScopeFactory scopes, TimeSpan every, Func<IServiceProvider, CancellationToken, Task> pass,
        ILogger logger, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(every);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await pass(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Background pass failed.");
            }

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
