using SmartRail.Api.Modules.Catering.Orders;
using SmartRail.Api.Modules.Catering.Outbox;
using SmartRail.Api.Modules.Catering.Provider;

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
            builder.Services.AddHttpClient<IRailBitesClient, RailBitesClient>(client =>
                client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("RailBites:TimeoutSeconds", 5)));
            builder.Services.AddMemoryCache();

            builder.Services.Configure<PlacementOptions>(builder.Configuration.GetSection(PlacementOptions.Section));
            builder.Services.AddScoped<OrderLinkWrites>();
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
    }
}
