using SmartRail.Api.Modules.Operations.Changes;

namespace SmartRail.Api.Modules.Operations;

public static class OperationsSetup
{
    public static void AddSmartRailOperations(this WebApplicationBuilder builder, bool hasDatabase)
    {
        if (hasDatabase)
        {
            builder.Services.AddScoped<ChangeStore>();
            builder.Services.AddScoped<ChangeConfirmer>();
        }
    }

    public static void MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPublicChanges();
        app.MapStaffChanges();
    }
}
