using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Catering;
using SmartRail.Api.Modules.Catering.Provider;
using SmartRail.Api.Modules.Rail.Running;

namespace SmartRail.Api.Modules.Demo;

// The staff-facing demo adapter. Program.cs maps it only in Development, and every route needs
// simulation.control, rechecked from the database on each call. The browser never receives the
// simulator's internal service key or any provider credential: train actions call the in-process
// SimulatorControl, and provider actions use the configured server-side client with its own key.
// Every action is an idempotent assignment of a desired state, never a toggle.
public static class DemoSetup
{
    public const string PauseAction = "demo.simulation.pause";
    public const string ResumeAction = "demo.simulation.resume";
    public const string OutageAction = "demo.provider.outage";

    public static void AddSmartRailDemo(this WebApplicationBuilder builder, bool hasDatabase)
    {
        if (hasDatabase)
        {
            // The same check /health/ready runs, so the panel's diagnostics read is the real one.
            builder.Services.AddScoped<CateringHealthCheck>();
        }
    }

    public static void MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        var demo = app.MapGroup("/api/demo")
            .RequireAuthorization(AccessPolicies.For(Permissions.SimulationControl));

        demo.MapGet("journeys/{journeyId:guid}/simulation", ReadSimulationAsync);
        demo.MapPost("journeys/{journeyId:guid}/pause", PauseAsync);
        demo.MapPost("journeys/{journeyId:guid}/resume", ResumeAsync);
        demo.MapGet("provider/outage", ReadOutageAsync);
        demo.MapPut("provider/outage", SetOutageAsync);
        demo.MapGet("diagnostics", ReadDiagnosticsAsync);
    }

    private static Task<IResult> PauseAsync(
        Guid journeyId, HttpContext context, SmartRailDbContext db, SimulatorControl control,
        SimulatorOptions options, CurrentAccount current, AccessGuard guard, CancellationToken cancellationToken) =>
        SetPausedAsync(journeyId, paused: true, context, db, control, options, current, guard, cancellationToken);

    private static Task<IResult> ResumeAsync(
        Guid journeyId, HttpContext context, SmartRailDbContext db, SimulatorControl control,
        SimulatorOptions options, CurrentAccount current, AccessGuard guard, CancellationToken cancellationToken) =>
        SetPausedAsync(journeyId, paused: false, context, db, control, options, current, guard, cancellationToken);

    // The panel's view of one journey: whether the simulator is on and whether this journey's
    // reports are paused. A paused flag is never evidence a real train stopped.
    private static async Task<IResult> ReadSimulationAsync(
        Guid journeyId, SmartRailDbContext db, SimulatorControl control, SimulatorOptions options,
        CancellationToken cancellationToken)
    {
        if (!await db.Journeys.AnyAsync(j => j.Id == journeyId, cancellationToken))
        {
            return UnknownJourney();
        }

        return Results.Ok(new SimulationStateDto(journeyId, options.Enabled, control.IsPaused(journeyId)));
    }

    private static async Task<IResult> SetPausedAsync(
        Guid journeyId, bool paused, HttpContext context, SmartRailDbContext db, SimulatorControl control,
        SimulatorOptions options, CurrentAccount current, AccessGuard guard, CancellationToken cancellationToken)
    {
        if (!await db.Journeys.AnyAsync(j => j.Id == journeyId, cancellationToken))
        {
            return UnknownJourney();
        }

        // The pause list exists without the worker, but changing it would have no effect: say so
        // rather than pretend the train responded.
        if (!options.Enabled)
        {
            return Results.Problem(
                title: "Simulator disabled",
                detail: "The simulated-running worker is off, so pause and resume have no effect. Set Simulator:Enabled and restart the API.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["code"] = "SIMULATOR_DISABLED" });
        }

        if (paused)
        {
            control.Pause(journeyId);
        }
        else
        {
            control.Resume(journeyId);
        }

        var actor = (await current.GetAsync(context.User, cancellationToken))!;
        db.AuditEntries.Add(guard.NewEntry(
            context, actor.Id, paused ? PauseAction : ResumeAction, AuditOutcomes.Succeeded, "JOURNEY", journeyId));
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new SimulationStateDto(journeyId, options.Enabled, paused));
    }

    private static async Task<IResult> ReadOutageAsync(IGrilledClient provider, CancellationToken cancellationToken)
    {
        var result = await provider.GetOutageAsync(cancellationToken);
        return result.Outcome == ProviderOutcome.Success
            ? Results.Ok(new ProviderOutageDto(result.Value!.Outage))
            : ProviderUnavailable();
    }

    private static async Task<IResult> SetOutageAsync(
        OutageRequest? request, HttpContext context, IGrilledClient provider, SmartRailDbContext db,
        CurrentAccount current, AccessGuard guard, CancellationToken cancellationToken)
    {
        if (request?.Active is not { } active)
        {
            return Results.Problem(
                title: "Invalid outage request",
                detail: "active must be true or false.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The restore call is not gated on readiness: a simulated outage keeps this route reachable.
        var result = await provider.SetOutageAsync(active, cancellationToken);
        if (result.Outcome != ProviderOutcome.Success)
        {
            return ProviderUnavailable();
        }

        var actor = (await current.GetAsync(context.User, cancellationToken))!;
        db.AuditEntries.Add(guard.NewEntry(context, actor.Id, OutageAction, AuditOutcomes.Succeeded, "PROVIDER", null));
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new ProviderOutageDto(result.Value!.Outage));
    }

    // The same dependency and backlog numbers /health/ready reports, so the panel never grows a
    // second, drifting source. Counts and ages only; no order, code or payload is exposed.
    private static async Task<IResult> ReadDiagnosticsAsync(CateringHealthCheck catering, CancellationToken cancellationToken)
    {
        var result = await catering.CheckHealthAsync(new HealthCheckContext(), cancellationToken);
        return Results.Ok(new DiagnosticsDto(result.Status.ToString(), result.Description, result.Data));
    }

    private static IResult UnknownJourney() =>
        Results.Problem(title: "Unknown journey", detail: "No journey has this ID.",
            statusCode: StatusCodes.Status404NotFound);

    private static IResult ProviderUnavailable() =>
        Results.Problem(
            title: "Provider unavailable",
            detail: "Grilled did not answer the outage switch. If its process has stopped, start it from Aspire; a simulated outage keeps this route reachable.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?> { ["code"] = "PROVIDER_UNAVAILABLE" });

    private sealed record SimulationStateDto(Guid JourneyId, bool Enabled, bool Paused);

    private sealed record ProviderOutageDto(bool Active);

    private sealed record OutageRequest(bool? Active);

    private sealed record DiagnosticsDto(string Status, string? Description, IReadOnlyDictionary<string, object> Data);
}
