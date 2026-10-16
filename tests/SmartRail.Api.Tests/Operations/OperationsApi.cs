using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Tests.Access;

namespace SmartRail.Api.Tests.Operations;

// Helpers shared by the Operations endpoint tests: a journey with its stops, people with the
// roles under test, and calls to the staff routes.
public sealed record StopRef(Guid StopId, Guid StationId, string StationName);

public static class OperationsApiExtensions
{
    public static async Task<(Guid JourneyId, List<StopRef> Stops)> JourneyWithStopsAsync(this AccessApi api, int skip = 0)
    {
        var journeyId = await api.JourneyIdAsync(skip);
        var stops = new List<StopRef>();
        await api.WithDbAsync(async db =>
            stops = await db.Journeys.Where(j => j.Id == journeyId)
                .SelectMany(j => j.Pattern.Stops).OrderBy(s => s.Sequence)
                .Select(s => new StopRef(s.Id, s.StationId, db.Stations.Single(st => st.Id == s.StationId).NameEn))
                .ToListAsync());
        return (journeyId, stops);
    }

    public static Task<HttpResponseMessage> PostAsync(this AccessApi api, string? subject, string path, object? body)
    {
        var client = api.ClientFor(subject is null ? null : api.Token(subject));
        return client.PostAsJsonAsync(path, body);
    }

    public static Task<HttpResponseMessage> PreviewAsync(this AccessApi api, string? subject, Guid journeyId, object? body) =>
        api.PostAsync(subject, $"/api/operations/journeys/{journeyId}/changes/preview", body);

    public static Task<HttpResponseMessage> ConfirmAsync(this AccessApi api, string? subject, Guid journeyId, object? body) =>
        api.PostAsync(subject, $"/api/operations/journeys/{journeyId}/changes", body);

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    public static object WithVersion(this object request, int version)
    {
        var json = JsonSerializer.SerializeToElement(request);
        var fields = json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        fields["previewedVersion"] = version;
        return fields;
    }

    public static async Task<(int Version, int Changes)> CountsAsync(this AccessApi api, Guid journeyId)
    {
        var result = (0, 0);
        await api.WithDbAsync(async db =>
            result = (
                (await db.JourneyVersions.SingleAsync(v => v.JourneyId == journeyId)).Version,
                await db.ServiceChanges.CountAsync(c => c.JourneyId == journeyId)));
        return result;
    }

    // Writes a published change straight into history, as an earlier confirmation would have.
    public static async Task<Guid> SeedChangeAsync(
        this AccessApi api, Guid journeyId, int version, Guid publisher, ChangeDetails details,
        ChangeAction action = ChangeAction.Publish, Guid? supersedes = null)
    {
        var id = Guid.NewGuid();
        await api.WithDbAsync(async db =>
        {
            db.ServiceChanges.Add(new ServiceChange(id, version, action, details, supersedes)
            {
                JourneyId = journeyId, PublishedById = publisher, PublishedAt = DateTimeOffset.UtcNow,
            });
            (await db.JourneyVersions.SingleAsync(v => v.JourneyId == journeyId)).Version = version;
            await db.SaveChangesAsync();
        });
        return id;
    }
}

// Request bodies as a staff client sends them.
public static class ChangeBodies
{
    public static object Delay(int minutes) => new { draft = new { kind = "DELAY", delayMinutes = minutes } };

    public static object Cancel() => new { draft = new { kind = "CANCEL" } };

    public static object Platform(Guid stopId, string platform) =>
        new { draft = new { kind = "PLATFORM", targetStopId = stopId, newPlatform = platform } };

    public static object EarlyEnd(Guid stopId) => new { draft = new { kind = "EARLY_END", finalStopId = stopId } };

    public static object Withdraw(Guid changeId) => new { withdrawChangeId = changeId };
}
