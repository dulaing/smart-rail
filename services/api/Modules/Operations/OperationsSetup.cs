using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Briefings;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Operations.Incidents;
using SmartRail.Api.Modules.Operations.Reports;
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
            builder.Services.AddScoped<IPersonalChangeReader, PersonalChangeReader>();
            builder.Services.AddScoped<IJourneyEligibilityReader, JourneyEligibilityReader>();
            builder.Services.AddScoped<ReportStore>();
            builder.Services.AddScoped<ReportSubmitter>();
            builder.Services.AddScoped<ReportAcknowledger>();
            builder.Services.AddScoped<IncidentStore>();
            builder.Services.AddScoped<IncidentLinks>();
            builder.Services.AddScoped<IncidentOpener>();
            builder.Services.AddScoped<IncidentResolver>();
            builder.Services.AddScoped<JourneyAuditTargets>();
        }
    }

    public static void MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPublicChanges();
        app.MapStaffChanges();
        app.MapDriverBriefings();
        app.MapDriverReports();
        app.MapControlReports();
        app.MapIncidents();
    }
}
