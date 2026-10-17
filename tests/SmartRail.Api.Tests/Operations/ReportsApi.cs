using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Reports;
using SmartRail.Api.Tests.Access;

namespace SmartRail.Api.Tests.Operations;

// Helpers shared by the driver report tests: people with the roles under test and calls to the
// driver routes.
public static class ReportsApiExtensions
{
    public static string ReportsPath(Guid journeyId) => $"/api/driver/journeys/{journeyId}/reports";

    public static Task<HttpResponseMessage> GetAsync(this AccessApi api, string? subject, string path) =>
        api.ClientFor(subject is null ? null : api.Token(subject)).GetAsync(path);

    // A POST with no body, such as an acknowledgement or a resolution.
    public static Task<HttpResponseMessage> PostEmptyAsync(this AccessApi api, string? subject, string path) =>
        api.ClientFor(subject is null ? null : api.Token(subject)).PostAsync(path, null);

    public static Task<HttpResponseMessage> SubmitReportAsync(this AccessApi api, string? subject, Guid journeyId, object? body) =>
        api.PostAsync(subject, ReportsPath(journeyId), body);

    public static object Report(Guid clientReportId, string reasonCode = "SIGNAL_DELAY") =>
        new { clientReportId, reasonCode };

    // A driver account assigned to the journeys given.
    public static async Task<Guid> AddDriverAsync(
        this AccessApi api, string subject, string displayName = "Sunil Driver", params Guid[] journeyIds)
    {
        var id = await api.AddAccountAsync(subject, displayName, roles: Roles.Driver);
        foreach (var journeyId in journeyIds)
        {
            await api.AssignAsync(id, journeyId);
        }

        return id;
    }

    public static async Task<List<DriverReport>> ReportsOnAsync(this AccessApi api, Guid journeyId)
    {
        var reports = new List<DriverReport>();
        await api.WithDbAsync(async db =>
            reports = await db.DriverReports.AsNoTracking().Where(r => r.JourneyId == journeyId)
                .OrderBy(r => r.ReceivedAt).ThenBy(r => r.Id).ToListAsync());
        return reports;
    }

    // Successful audit rows about one report, optionally only those for one action.
    public static async Task<int> ReportAuditCountAsync(this AccessApi api, Guid reportId, string? action = null)
    {
        var count = 0;
        await api.WithDbAsync(async db =>
            count = await db.AuditEntries.CountAsync(e =>
                e.TargetType == "REPORT" && e.TargetId == reportId && e.Outcome == "SUCCEEDED" && (action == null || e.Action == action)));
        return count;
    }

    // Stores a controller's acknowledgement directly, as the acknowledgement route will in a later task.
    public static Task AcknowledgeAsync(this AccessApi api, Guid reportId, Guid controllerId, DateTimeOffset at) =>
        api.WithDbAsync(db => db.DriverReports.Where(r => r.Id == reportId).ExecuteUpdateAsync(set => set
            .SetProperty(r => r.AcknowledgedById, controllerId)
            .SetProperty(r => r.AcknowledgedAt, at)));

    // A POST sent to a copy of the API whose services have been replaced, over the same database.
    public static Task<HttpResponseMessage> PostWithAsync(
        this AccessApi api, Action<IServiceCollection> replace, string subject, string path, object body)
    {
        var factory = api.Factory.WithWebHostBuilder(host => host.ConfigureTestServices(replace));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", api.Token(subject));
        return client.PostAsJsonAsync(path, body);
    }
}
