using SmartRail.Api.Modules.Catering.Outbox;

namespace SmartRail.Api.Modules.Catering;

public static class CateringSetup
{
    public static void AddSmartRailCatering(this WebApplicationBuilder builder, bool hasDatabase)
    {
        if (hasDatabase)
        {
            builder.Services.AddScoped<ICateringOutbox, CateringOutbox>();
        }
    }
}
