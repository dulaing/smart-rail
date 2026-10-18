using Microsoft.Extensions.Options;

namespace SmartRail.Api.Modules.Catering.Outbox;

// Sends queued train changes to RailBites in the background.
public sealed class OutboxWorker(
    IServiceScopeFactory scopes, IOptions<OutboxOptions> options, ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        options.Value.WorkerEnabled
            ? BackgroundLoop.RunAsync(
                scopes, TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds)),
                (services, token) => services.GetRequiredService<OutboxDispatcher>().RunDueAsync(token),
                logger, stoppingToken)
            : Task.CompletedTask;
}
