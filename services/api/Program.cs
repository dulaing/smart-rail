using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Rail;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Readiness checks the database. The AppHost sets the connection string; without it
// (plain `dotnet run`) there is nothing to check yet.
var healthChecks = builder.Services.AddHealthChecks();
var smartrailDb = builder.Configuration.GetConnectionString("smartrail");
if (!string.IsNullOrWhiteSpace(smartrailDb))
{
    healthChecks.AddNpgSql(smartrailDb, name: "smartrail-db", tags: ["ready"]);
}

// Console logs always work. Traces and logs are also sent over OTLP (for example to the
// Aspire dashboard) only when OTEL_EXPORTER_OTLP_ENDPOINT is set.
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

var telemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("smartrail-api"))
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation());

if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    telemetry.UseOtlpExporter();
}

builder.Services.TryAddSingleton(TimeProvider.System);
var hasDatabase = builder.AddSmartRailDatabase();

var app = builder.Build();

// `dotnet run --project services/api -- --migrate-and-seed` prepares the database, then exits.
if (args.Contains("--migrate-and-seed"))
{
    await app.MigrateAndSeedAsync();
    return;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (hasDatabase)
{
    app.MapRailEndpoints();
}

app.UseHttpsRedirection();

// Live only says the process is running, so it runs no checks.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

public partial class Program;
