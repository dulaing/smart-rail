using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Operations;

public static class OperationsSetup
{
    public static void AddSmartRailOperations(this WebApplicationBuilder builder, bool hasDatabase)
    {
        if (hasDatabase)
        {
            builder.Services.AddScoped<ChangeStore>();
            builder.Services.AddScoped<ChangeConfirmer>();
            builder.Services.AddScoped<IConfirmedChangeReader, ConfirmedChangeReader>();
        }
    }

    public static void MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPublicChanges();
        app.MapStaffChanges();
    }
}
