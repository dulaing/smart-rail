using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Tests.Access;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Operations;

// Durable version rows, append-only history and the public confirmed-state read, on a
// disposable PostgreSQL.
public class ConfirmedStateApiTests(AccessApi api) : IClassFixture<AccessApi>
{
    private async Task<(Guid JourneyId, List<Guid> StopIds)> JourneyWithStopsAsync(int skip = 0)
    {
        var journeyId = await api.JourneyIdAsync(skip);
        var stops = new List<Guid>();
        await api.WithDbAsync(async db =>
            stops = await db.Journeys.Where(j => j.Id == journeyId)
                .SelectMany(j => j.Pattern.Stops).OrderBy(s => s.Sequence).Select(s => s.Id).ToListAsync());
        return (journeyId, stops);
    }

    // Stores a history row and moves the journey to that version, as a publish will.
    private async Task AddChangeAsync(Guid journeyId, int version, Guid publisher, ChangeDetails details, Guid? supersedes = null,
        ChangeAction action = ChangeAction.Publish, Guid? id = null)
    {
        var change = new ServiceChange(id ?? Guid.NewGuid(), version, action, details, supersedes)
        {
            JourneyId = journeyId,
            PublishedById = publisher,
            PublishedAt = DateTimeOffset.UtcNow,
        };
        await api.WithDbAsync(async db =>
        {
            db.ServiceChanges.Add(change);
            (await db.JourneyVersions.SingleAsync(v => v.JourneyId == journeyId)).Version = version;
            await db.SaveChangesAsync();
        });
    }

    private async Task<(HttpResponseMessage Response, JsonElement Body)> ReadStateAsync(Guid journeyId, string? etag = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/journeys/{journeyId}/changes");
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        var response = await api.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement);
    }

    [RequiresDatabaseFact]
    public async Task Every_journey_has_one_version_row_and_reseeding_never_resets_it()
    {
        var (journeyId, _) = await JourneyWithStopsAsync();
        await api.MigrateAndSeedAsync();

        var counts = (Journeys: 0, Versions: 0, Distinct: 0);
        await api.WithDbAsync(async db =>
            counts = (await db.Journeys.CountAsync(), await db.JourneyVersions.CountAsync(),
                await db.JourneyVersions.Select(v => v.JourneyId).Distinct().CountAsync()));
        Assert.True(counts.Journeys > 0);
        Assert.Equal(counts.Journeys, counts.Versions);
        Assert.Equal(counts.Versions, counts.Distinct);

        // A version that has moved on survives another migrate-and-seed, unchanged.
        var publisher = await api.AddAccountAsync("seed-controller", roles: Roles.Controller);
        await AddChangeAsync(journeyId, 1, publisher, ChangeDetails.Cancel());
        JourneyChangeVersion before = null!;
        await api.WithDbAsync(async db => before = await db.JourneyVersions.AsNoTracking().SingleAsync(v => v.JourneyId == journeyId));

        await api.MigrateAndSeedAsync();
        await api.MigrateAndSeedAsync();

        JourneyChangeVersion after = null!;
        var total = 0;
        await api.WithDbAsync(async db =>
        {
            after = await db.JourneyVersions.AsNoTracking().SingleAsync(v => v.JourneyId == journeyId);
            total = await db.JourneyVersions.CountAsync();
        });
        Assert.Equal(1, after.Version);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(counts.Versions, total);
    }

    [RequiresDatabaseFact]
    public async Task History_is_append_only_and_unique_per_journey_version()
    {
        var (journeyId, stops) = await JourneyWithStopsAsync(1);
        var publisher = await api.AddAccountAsync("history-controller", roles: Roles.Controller);
        var delay = Guid.NewGuid();
        await AddChangeAsync(journeyId, 1, publisher, ChangeDetails.Delay(10), id: delay);
        await AddChangeAsync(journeyId, 2, publisher, ChangeDetails.EarlyEnd(stops[1]));

        // The same journey version cannot be written twice.
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            AddChangeAsync(journeyId, 2, publisher, ChangeDetails.Delay(30)));

        // Nothing already written can be edited or removed.
        await api.WithDbAsync(async db =>
        {
            var edit = await Assert.ThrowsAnyAsync<Exception>(() =>
                db.Database.ExecuteSqlRawAsync("UPDATE changes.service_changes SET delay_minutes = 99 WHERE id = {0}", delay));
            Assert.Contains("append-only", edit.ToString());
            var remove = await Assert.ThrowsAnyAsync<Exception>(() =>
                db.Database.ExecuteSqlRawAsync("DELETE FROM changes.service_changes WHERE id = {0}", delay));
            Assert.Contains("append-only", remove.ToString());
        });

        var versions = new List<int>();
        await api.WithDbAsync(async db =>
            versions = await db.ServiceChanges.Where(c => c.JourneyId == journeyId).OrderBy(c => c.JourneyVersion)
                .Select(c => c.JourneyVersion).ToListAsync());
        Assert.Equal([1, 2], versions);
    }

    [RequiresDatabaseFact]
    public async Task The_database_rejects_a_change_with_the_wrong_fields_for_its_kind()
    {
        var (journeyId, stops) = await JourneyWithStopsAsync(2);
        var publisher = await api.AddAccountAsync("fields-controller", roles: Roles.Controller);

        // A cancellation with a delay, and a stop that is not in the timetable.
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            AddChangeAsync(journeyId, 1, publisher, new ChangeDetails(ChangeKind.Cancel, DelayMinutes: 5)));
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            AddChangeAsync(journeyId, 1, publisher, ChangeDetails.EarlyEnd(Guid.NewGuid())));
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            AddChangeAsync(journeyId, 1, publisher, ChangeDetails.Platform(stops[0], "  ")));
    }

    [RequiresDatabaseFact]
    public async Task A_journey_with_no_changes_is_served_as_scheduled_and_a_stranger_is_404()
    {
        var (journeyId, stops) = await JourneyWithStopsAsync(3);

        var (response, body) = await ReadStateAsync(journeyId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.GetProperty("journeyVersion").GetInt32());
        Assert.Equal("AS_SCHEDULED", body.GetProperty("availability").GetString());
        Assert.Equal(0, body.GetProperty("activeChanges").GetArrayLength());
        Assert.Equal(stops, body.GetProperty("stops").EnumerateArray().Select(s => s.GetProperty("stopId").GetGuid()));
        Assert.All(body.GetProperty("stops").EnumerateArray(), s => Assert.True(s.GetProperty("served").GetBoolean()));

        Assert.Equal(HttpStatusCode.NotFound, (await ReadStateAsync(Guid.NewGuid())).Response.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task The_public_state_shows_served_and_unserved_stops_without_staff_identity()
    {
        var (journeyId, stops) = await JourneyWithStopsAsync(4);
        var publisher = await api.AddAccountAsync("public-controller", "Secret Name", roles: Roles.Controller);
        await AddChangeAsync(journeyId, 1, publisher, ChangeDetails.EarlyEnd(stops[1]));
        await AddChangeAsync(journeyId, 2, publisher, ChangeDetails.Platform(stops[0], "9"));

        var (response, body) = await ReadStateAsync(journeyId);
        var text = body.GetRawText();

        Assert.Equal(2, body.GetProperty("journeyVersion").GetInt32());
        Assert.Equal("ENDS_EARLY", body.GetProperty("availability").GetString());
        Assert.Equal(stops[1], body.GetProperty("finalStopId").GetGuid());
        Assert.Equal(
            stops.Select((_, i) => i <= 1),
            body.GetProperty("stops").EnumerateArray().Select(s => s.GetProperty("served").GetBoolean()));
        Assert.Equal("9", body.GetProperty("stops")[0].GetProperty("platform").GetString());
        Assert.Equal(2, body.GetProperty("activeChanges").GetArrayLength());
        Assert.NotEqual(default, body.GetProperty("activeChanges")[0].GetProperty("confirmedAt").GetDateTimeOffset());

        Assert.DoesNotContain(publisher.ToString(), text);
        Assert.DoesNotContain("Secret Name", text);
        Assert.DoesNotContain("publishedBy", text, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(response.Headers.ETag);
    }

    [RequiresDatabaseFact]
    public async Task The_state_is_304_until_it_changes_and_then_has_a_new_etag()
    {
        var (journeyId, stops) = await JourneyWithStopsAsync(5);
        var publisher = await api.AddAccountAsync("etag-controller", roles: Roles.Controller);

        var first = await ReadStateAsync(journeyId);
        var etag = first.Response.Headers.ETag!.Tag;
        Assert.Equal(HttpStatusCode.NotModified, (await ReadStateAsync(journeyId, etag)).Response.StatusCode);

        await AddChangeAsync(journeyId, 1, publisher, ChangeDetails.Delay(25));
        var changed = await ReadStateAsync(journeyId, etag);

        Assert.Equal(HttpStatusCode.OK, changed.Response.StatusCode);
        Assert.NotEqual(etag, changed.Response.Headers.ETag!.Tag);
        Assert.Equal(25, changed.Body.GetProperty("confirmedDelayMinutes").GetInt32());

        // Withdrawing it changes what is represented again.
        var delayId = changed.Body.GetProperty("activeChanges")[0].GetProperty("changeId").GetGuid();
        await AddChangeAsync(journeyId, 2, publisher, new ChangeDetails(ChangeKind.Delay), delayId, ChangeAction.Withdraw);
        var withdrawn = await ReadStateAsync(journeyId, changed.Response.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.OK, withdrawn.Response.StatusCode);
        Assert.Equal(JsonValueKind.Null, withdrawn.Body.GetProperty("confirmedDelayMinutes").ValueKind);
        Assert.Equal(0, withdrawn.Body.GetProperty("activeChanges").GetArrayLength());
        Assert.Equal(stops.Count, withdrawn.Body.GetProperty("stops").GetArrayLength());
    }
}
