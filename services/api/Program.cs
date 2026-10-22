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
using Microsoft.OpenApi;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// This is a mode flag, not a configuration key/value pair. Leave subsequent seed settings
// available to the standard configuration parser.
var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--seed-operations-demo").ToArray());

// The document lists every route and offers an Authorize button for a Keycloak access token.
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "SmartRail API";
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "A Keycloak access token for the smartrail realm.",
    };
    document.Security ??= [];
    document.Security.Add(new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
    });
    return Task.CompletedTask;
}));

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
        // Outgoing calls to Grilled carry the caller's trace, so one trace spans both services.
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

// Explicit local fixtures; ordinary startup and production never seed Operations evidence.
if (args.Contains("--seed-operations-demo"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Operations demo data can only be seeded in the Development environment.");
    }

    var time = app.Services.GetRequiredService<TimeProvider>();
    var anchorText = app.Configuration["Seed:OperationsAnchor"];
    var anchor = time.GetUtcNow();
    if (anchorText is not null && !InstantText.TryParse(anchorText, out anchor))
    {
        throw new InvalidOperationException("Seed:OperationsAnchor must be an ISO timestamp with Z or an explicit offset.");
    }

    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<SmartRailDbContext>();
    var journeys = await OperationsDemoSeeder.SeedAsync(db, time, anchor);
    Console.WriteLine($"Operations seed anchor: {DateTimeOffset.FromUnixTimeSeconds(anchor.ToUnixTimeSeconds()):O}");
    foreach (var journey in journeys)
    {
        Console.WriteLine($"{journey.TrainNumber} | service date {journey.ServiceDate:yyyy-MM-dd} | /journeys/{journey.JourneyId}");
    }
    return;
}

// `dotnet run --project services/api -- --migrate-and-seed` prepares the database, then exits.
if (args.Contains("--migrate-and-seed"))
{
    await app.MigrateAndSeedAsync();
    return;
}

// `dotnet run --project services/api -- --seed-demo --anchor <ISO>` prepares the Development
// presentation scenario for the anchor's Colombo service date and writes its manifest. Run it
// from the repository root so the manifest lands in artifacts/. Identities and provider data are
// separate commands: --seed-demo-identities (needs Keycloak) and the Grilled seed.
if (args.Contains("--seed-demo"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("The demo scenario can only be seeded in the Development environment.");
    }

    if (!PilotScenario.TryParseAnchor(ArgValue(args, "--anchor"), out var anchor, out var anchorError))
    {
        Console.Error.WriteLine(anchorError);
        Environment.ExitCode = 2;
        return;
    }

    var run = ArgValue(args, "--run") ?? PilotScenario.DefaultRun(anchor);
    var manifestPath = ArgValue(args, "--manifest")
        ?? Path.Combine("artifacts", $"pilot-scenario-{PilotScenario.SafeRun(run)}.json");

    await app.MigrateAndSeedAsync(PilotScenario.ServiceDateOf(anchor));

    await using (var scope = app.Services.CreateAsyncScope())
    {
        var manifest = await PilotScenario.BuildManifestAsync(
            scope.ServiceProvider.GetRequiredService<SmartRailDbContext>(), anchor, run);
        await PilotScenario.WriteManifestAsync(manifestPath, manifest);
        PilotScenario.PrintSummary(manifest, manifestPath);
    }

    return;
}

// Reads `--name value` or `--name=value` from the command line.
static string? ArgValue(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == name)
        {
            return i + 1 < args.Length ? args[i + 1] : null;
        }

        if (args[i].StartsWith(name + "=", StringComparison.Ordinal))
        {
            return args[i][(name.Length + 1)..];
        }
    }

    return null;
}

app.UseHttpsRedirection();
app.UseCors();

// Swagger UI at /swagger reads the OpenAPI document. It must come before UseAuthorization, whose
// deny-by-default fallback policy would otherwise answer 401 for its static files.
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(ui =>
    {
        ui.SwaggerEndpoint("/openapi/v1.json", "SmartRail API v1");
        ui.RoutePrefix = "swagger";
    });
}

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
    app.MapPassengerEndpoints();
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
