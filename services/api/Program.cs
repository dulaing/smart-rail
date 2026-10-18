using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Catering;
using SmartRail.Api.Modules.Catering.Callbacks;
using SmartRail.Api.Modules.Demo;
using SmartRail.Api.Modules.Operations;
using SmartRail.Api.Modules.Rail;
using SmartRail.Api.Modules.Rail.Running;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
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
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        // Outgoing calls to RailBites carry the caller's trace, so one trace spans both services.
        .AddHttpClientInstrumentation()
        .AddSource(CateringMetrics.ActivitySourceName))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter(CateringMetrics.MeterName));

if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    telemetry.UseOtlpExporter();
}

builder.Services.TryAddSingleton(TimeProvider.System);
var hasDatabase = builder.AddSmartRailDatabase();
builder.AddSmartRailAccess(hasDatabase);
builder.AddSmartRailRail(hasDatabase);
builder.AddSmartRailCatering(hasDatabase);
builder.AddSmartRailOperations(hasDatabase);
builder.AddSmartRailSimulator(hasDatabase);
builder.AddSmartRailDemo(hasDatabase);

// Browsers may call the API only from the origins listed in Cors:AllowedOrigins.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
    .WithHeaders("Authorization", "Content-Type", "If-None-Match")
    .WithExposedHeaders("ETag", RunningEndpoints.ServerTimeHeader)));

var app = builder.Build();

// `dotnet run --project services/api -- --migrate-and-seed` prepares the database, then exits.
if (args.Contains("--migrate-and-seed"))
{
    await app.MigrateAndSeedAsync();
    return;
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// `dotnet run --project services/api -- --seed-demo-identities` creates synthetic Keycloak users
// and their local accounts for local development, then exits.
if (args.Contains("--seed-demo-identities"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Demo identities can only be seeded in the Development environment.");
    }

    await using var scope = app.Services.CreateAsyncScope();
    await DemoIdentitySeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<SmartRailDbContext>(), app.Configuration);
    return;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

if (hasDatabase)
{
    app.MapRailEndpoints();
    app.MapRunningEndpoints();
    app.MapAccessEndpoints();
    app.MapAccessAdminEndpoints();
    app.MapOperationsEndpoints();
    app.MapCateringEndpoints();

    // Demo-only controls: never available outside Development.
    if (app.Environment.IsDevelopment())
    {
        app.MapSimulatorEndpoints();
        app.MapDemoEndpoints();
    }
}

// Live only says the process is running, so it runs no checks.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    // Degraded still answers 200: the API can serve train information. The body says what is degraded
    // and, for food ordering, how much is waiting.
    ResponseWriter = HealthResponseWriter.WriteAsync,
}).AllowAnonymous();

app.Run();

public partial class Program;
