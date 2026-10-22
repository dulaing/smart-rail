using SmartRail.Api.Modules.Catering.Callbacks;
using SmartRail.Api.Modules.Catering.Orders;
using SmartRail.Api.Modules.Catering.Outbox;
using SmartRail.Api.Modules.Catering.Provider;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartRail.Api.Modules.Catering;

public static class CateringSetup
{
    public static void AddSmartRailCatering(this WebApplicationBuilder builder, bool hasDatabase)
    {
        if (hasDatabase)
        {
            builder.Services.AddScoped<ICateringOutbox, CateringOutbox>();

            // A short timeout keeps a passenger's request from hanging on a stopped provider; the
            // placement stays pending and is retried.
            builder.Services.AddHttpClient<IGrilledClient, GrilledClient>(client =>
                client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Grilled:TimeoutSeconds", 5)));
            builder.Services.AddMemoryCache();
            builder.Services.AddHealthChecks().AddCheck<CateringHealthCheck>("catering", failureStatus: HealthStatus.Degraded, tags: ["ready"]);

            builder.Services.Configure<PlacementOptions>(builder.Configuration.GetSection(PlacementOptions.Section));
            builder.Services.AddScoped<OrderLinkWrites>();
            builder.Services.AddScoped<OrderUpdateReceiver>();
            builder.Services.AddScoped<PickupAssessor>();
            builder.Services.AddScoped<PlacementProcessor>();
            builder.Services.AddScoped<OrderCreator>();
            builder.Services.AddScoped<OrderCanceller>();
            builder.Services.AddHostedService<PlacementWorker>();

            builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.Section));
            builder.Services.AddScoped<OutboxUpgrader>();
            builder.Services.AddScoped<OutboxDispatcher>();
            builder.Services.AddHostedService<OutboxWorker>();
        }
    }

    public static void MapCateringEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapBrowse();
        app.MapOrders();
        app.MapCallbacks();
        app.MapPickupPassage();
    }
}
