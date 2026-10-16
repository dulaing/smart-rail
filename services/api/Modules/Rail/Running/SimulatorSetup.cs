using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Rail.Running;

// Runs the ticker every 30 seconds for as long as the API is up.
public sealed class SimulatorWorker(
    IServiceScopeFactory scopes, SimulatorTicker ticker, TimeProvider time, ILogger<SimulatorWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SimulatorPlan.Interval, time);
        try
        {
            do
            {
                await TickOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The API is shutting down.
        }
    }

    // A failed tick (for example the database is not ready yet) is logged and the next one tries again.
    private async Task TickOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SmartRailDbContext>();
            var summary = await ticker.TickAsync(db, stoppingToken);
            if (summary.Created + summary.Repeated + summary.Paused + summary.Rejected > 0)
            {
                logger.LogInformation(
                    "Simulator tick: {Created} new, {Repeated} repeated, {Paused} paused, {Rejected} rejected.",
                    summary.Created, summary.Repeated, summary.Paused, summary.Rejected);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Simulator tick failed; trying again at the next tick.");
        }
    }
}

public static class SimulatorSetup
{
    public const string EnabledSetting = "Simulator:Enabled";
    public const string DelaySetting = "Simulator:DemoDelaySeconds";

    // The pause list and ticker always exist; the worker runs only when Simulator:Enabled is true.
    public static void AddSmartRailSimulator(this WebApplicationBuilder builder, bool hasDatabase)
    {
        var enabled = builder.Configuration.GetValue(EnabledSetting, false);
        var delay = builder.Configuration.GetValue(DelaySetting, 0);
        if (delay < 0)
        {
            throw new InvalidOperationException($"{DelaySetting} must be zero or more, but is {delay}.");
        }

        builder.Services.AddSingleton<SimulatorControl>();
        builder.Services.AddSingleton(new SimulatorOptions(enabled, delay));
        builder.Services.AddSingleton<SimulatorTicker>();
        if (enabled && hasDatabase)
        {
            builder.Services.AddHostedService<SimulatorWorker>();
        }
    }

    // Development only, and guarded by the service key. Pausing stops a journey's reports; the
    // server's clock keeps going, so the journey ages into unknown like a real silent train.
    public static void MapSimulatorEndpoints(this IEndpointRouteBuilder app)
    {
        var journeys = app.MapGroup("/internal/rail/simulator/journeys/{journeyId}")
            .AllowAnonymous()
            .AddEndpointFilter<ServiceKeyFilter>();
        journeys.MapPost("/pause", (string journeyId, SmartRailDbContext db, SimulatorControl control, CancellationToken ct) =>
            SetPausedAsync(journeyId, paused: true, db, control, ct));
        journeys.MapPost("/resume", (string journeyId, SmartRailDbContext db, SimulatorControl control, CancellationToken ct) =>
            SetPausedAsync(journeyId, paused: false, db, control, ct));
    }

    private static async Task<IResult> SetPausedAsync(
        string journeyId, bool paused, SmartRailDbContext db, SimulatorControl control, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(journeyId, out var id))
        {
            return Results.Problem(title: "Invalid journeyId", detail: "journeyId must be a journey ID.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!await db.Journeys.AnyAsync(j => j.Id == id, cancellationToken))
        {
            return Results.Problem(title: "Unknown journey", detail: "No journey has this ID.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (paused)
        {
            control.Pause(id);
        }
        else
        {
            control.Resume(id);
        }

        return Results.Ok(new { journeyId = id, paused });
    }
}
