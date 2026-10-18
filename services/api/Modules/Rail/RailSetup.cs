namespace SmartRail.Api.Modules.Rail;

public static class RailSetup
{
    // The doors other modules use to read Rail data. They need the database.
    public static void AddSmartRailRail(this WebApplicationBuilder builder, bool hasDatabase)
    {
        if (hasDatabase)
        {
            builder.Services.AddScoped<IJourneyStopReader, JourneyStopReader>();
            builder.Services.AddScoped<IPickupReader, PickupReader>();
        }
    }
}
