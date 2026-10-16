using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Api.Modules.Rail.Running;

namespace SmartRail.Api.Tests.Rail;

// A clock the test moves by hand, so no test sleeps or depends on the real time.
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public DateTimeOffset Now => _now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;

    public void Set(DateTimeOffset now) => _now = now;
}

// The API with a service key configured and a controllable clock.
public sealed class ObservationApi : TimetableApi
{
    public const string Key = "test-service-key";
    public const string Path = RunningEndpoints.ObservationsPath;

    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero));

    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        builder.UseSetting(ServiceKeyFilter.ConfigurationKey, Key);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    // A journey, its first three stops, in order.
    public async Task<(Guid Journey, Guid[] Stops)> JourneyAsync(int skip = 0)
    {
        var journey = Guid.Empty;
        Guid[] stops = [];
        await WithDbAsync(async db =>
        {
            var found = await db.Journeys.AsNoTracking()
                .Include(j => j.Pattern).ThenInclude(p => p.Stops)
                .OrderBy(j => j.DemoSeedKey).Skip(skip).FirstAsync();
            journey = found.Id;
            stops = found.Pattern.Stops.OrderBy(s => s.Sequence).Select(s => s.Id).Take(3).ToArray();
        });
        return (journey, stops);
    }

    // The first two stops of a pattern that is not the first journey's pattern.
    public async Task<Guid[]> StopsOfAnotherPatternAsync()
    {
        Guid[] stops = [];
        await WithDbAsync(async db =>
        {
            var journey = await db.Journeys.AsNoTracking().OrderBy(j => j.DemoSeedKey).FirstAsync();
            var other = await db.ServicePatterns.AsNoTracking()
                .Include(p => p.Stops)
                .OrderBy(p => p.TrainNumber)
                .FirstAsync(p => p.Id != journey.PatternId);
            stops = other.Stops.OrderBy(s => s.Sequence).Take(2).Select(s => s.Id).ToArray();
        });
        return stops;
    }

    public async Task<List<Observation>> RowsAsync(string? eventId = null, Guid? journeyId = null)
    {
        var rows = new List<Observation>();
        await WithDbAsync(async db => rows = await db.Observations.AsNoTracking()
            .Where(o => (eventId == null || o.SourceEventId == eventId) && (journeyId == null || o.JourneyId == journeyId))
            .OrderBy(o => o.ObservedAt).ToListAsync());
        return rows;
    }

    public string ObservedAgo(int seconds) => Clock.Now.AddSeconds(-seconds).ToOffset(TimeSpan.FromMinutes(330)).ToString("O");

    public Task<HttpResponseMessage> PostAsync(object body, string? key = Key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.Add(ServiceKeyFilter.Header, key);
        }

        return Client.SendAsync(request);
    }

    public Task<HttpResponseMessage> PostRawAsync(string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(ServiceKeyFilter.Header, Key);
        return Client.SendAsync(request);
    }

    // A valid body for the first segment of the journey.
    public async Task<Dictionary<string, object?>> BodyAsync(
        string eventId, int skip = 0, int progress = 400, int delay = 30, int ageSeconds = 10)
    {
        var (journey, stops) = await JourneyAsync(skip);
        return new Dictionary<string, object?>
        {
            ["sourceEventId"] = eventId,
            ["journeyId"] = journey,
            ["fromStopId"] = stops[0],
            ["toStopId"] = stops[1],
            ["progressPermille"] = progress,
            ["reportedDelaySeconds"] = delay,
            ["observedAt"] = ObservedAgo(ageSeconds),
        };
    }
}

// Needs a disposable PostgreSQL server (see RequiresDatabaseFactAttribute).
public class ObservationIngestionApiTests(ObservationApi api) : IClassFixture<ObservationApi>
{
    private static string NewEvent() => $"evt-{Guid.NewGuid():N}";

    [RequiresDatabaseFact]
    public async Task Missing_or_wrong_key_is_401_and_stores_nothing()
    {
        var eventId = NewEvent();
        var body = await api.BodyAsync(eventId);

        var anonymous = await api.PostAsync(body, key: null);
        var wrong = await api.PostAsync(body, key: "not-the-key");
        var empty = await api.PostAsync(body, key: "");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, empty.StatusCode);
        Assert.Empty(await api.RowsAsync(eventId));
    }

    [RequiresDatabaseFact]
    public async Task A_new_event_is_201_and_an_identical_retry_is_200_with_one_stored_row()
    {
        var eventId = NewEvent();
        var body = await api.BodyAsync(eventId, progress: 450, delay: 90, ageSeconds: 20);

        var created = await api.PostAsync(body);
        var retry = await api.PostAsync(body);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var first = await created.Content.ReadFromJsonAsync<JsonElement>();
        var second = await retry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());

        var row = Assert.Single(await api.RowsAsync(eventId));
        Assert.Equal(450, row.ProgressPermille);
        Assert.Equal(90, row.ReportedDelaySeconds);
    }

    [RequiresDatabaseFact]
    public async Task Receipt_time_is_set_by_the_server_and_observed_time_is_kept_as_sent()
    {
        var eventId = NewEvent();
        var body = await api.BodyAsync(eventId, ageSeconds: 45);
        body["receivedAt"] = "2000-01-01T00:00:00Z";   // a sender cannot choose this

        api.Clock.Advance(TimeSpan.FromSeconds(7));
        var response = await api.PostAsync(body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var row = Assert.Single(await api.RowsAsync(eventId));
        Assert.Equal(api.Clock.Now, row.ReceivedAt);
        Assert.Equal(api.Clock.Now.AddSeconds(-52), row.ObservedAt);
    }

    [RequiresDatabaseFact]
    public async Task The_ends_of_the_progress_range_are_accepted()
    {
        var atStart = await api.PostAsync(await api.BodyAsync(NewEvent(), progress: 0));
        var atStop = await api.PostAsync(await api.BodyAsync(NewEvent(), progress: 1000));

        Assert.Equal(HttpStatusCode.Created, atStart.StatusCode);
        Assert.Equal(HttpStatusCode.Created, atStop.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task Reusing_an_event_id_with_different_input_is_409_and_keeps_the_original()
    {
        var eventId = NewEvent();
        var original = await api.BodyAsync(eventId, progress: 300);
        var changed = await api.BodyAsync(eventId, progress: 700);

        Assert.Equal(HttpStatusCode.Created, (await api.PostAsync(original)).StatusCode);
        var conflict = await api.PostAsync(changed);

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var row = Assert.Single(await api.RowsAsync(eventId));
        Assert.Equal(300, row.ProgressPermille);
    }

    [RequiresDatabaseFact]
    public async Task Concurrent_identical_retries_store_one_row()
    {
        var eventId = NewEvent();
        var body = await api.BodyAsync(eventId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => api.PostAsync(body))));

        var statuses = responses.Select(r => r.StatusCode).ToList();
        Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(11, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Single(await api.RowsAsync(eventId));
    }

    [RequiresDatabaseFact]
    public async Task Concurrent_requests_reusing_a_key_with_different_input_have_one_winner()
    {
        var eventId = NewEvent();
        var bodies = new List<Dictionary<string, object?>>();
        for (var progress = 100; progress <= 800; progress += 100)
        {
            bodies.Add(await api.BodyAsync(eventId, progress: progress));
        }

        var responses = await Task.WhenAll(bodies.Select(b => Task.Run(() => api.PostAsync(b))));

        var statuses = responses.Select(r => r.StatusCode).ToList();
        Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(bodies.Count - 1, statuses.Count(s => s == HttpStatusCode.Conflict));
        Assert.Single(await api.RowsAsync(eventId));
    }

    [RequiresDatabaseFact]
    public async Task Unknown_journey_is_404()
    {
        var eventId = NewEvent();
        var body = await api.BodyAsync(eventId);
        body["journeyId"] = Guid.NewGuid();

        var response = await api.PostAsync(body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await api.RowsAsync(eventId));
    }

    [RequiresDatabaseFact]
    public async Task Invalid_input_is_400_and_stores_nothing()
    {
        var (_, stops) = await api.JourneyAsync();
        var elsewhere = await api.StopsOfAnotherPatternAsync();

        var cases = new Dictionary<string, Action<Dictionary<string, object?>>>
        {
            ["timestamp without an offset"] = b => b["observedAt"] = "2026-10-14T09:59:00",
            ["not a timestamp"] = b => b["observedAt"] = "yesterday",
            ["missing timestamp"] = b => b.Remove("observedAt"),
            ["future observation"] = b => b["observedAt"] = api.Clock.Now.AddSeconds(5).ToString("O"),
            ["progress above 1000"] = b => b["progressPermille"] = 1001,
            ["negative progress"] = b => b["progressPermille"] = -1,
            ["negative delay"] = b => b["reportedDelaySeconds"] = -1,
            ["missing delay"] = b => b.Remove("reportedDelaySeconds"),
            ["blank event id"] = b => b["sourceEventId"] = " ",
            ["segment skips a stop"] = b => b["toStopId"] = stops[2],
            ["segment is reversed"] = b => { b["fromStopId"] = stops[1]; b["toStopId"] = stops[0]; },
            ["segment from another pattern"] = b => { b["fromStopId"] = elsewhere[0]; b["toStopId"] = elsewhere[1]; },
            ["unknown stop"] = b => b["toStopId"] = Guid.NewGuid(),
        };

        foreach (var (name, change) in cases)
        {
            var eventId = NewEvent();
            var body = await api.BodyAsync(eventId);
            change(body);

            var response = await api.PostAsync(body);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{name}: got {(int)response.StatusCode}");
            Assert.Empty(await api.RowsAsync(eventId));
        }
    }

    [RequiresDatabaseFact]
    public async Task Malformed_json_and_an_empty_body_are_400()
    {
        var malformed = await api.PostRawAsync("{ \"sourceEventId\": ");
        var wrongType = await api.PostRawAsync("{ \"progressPermille\": \"high\" }");
        var empty = await api.PostRawAsync("{}");

        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task A_late_event_is_stored_as_history_without_replacing_newer_observed_evidence()
    {
        // A journey no other test in this class writes to.
        var (journey, _) = await api.JourneyAsync(skip: 3);
        var newer = await api.BodyAsync(NewEvent(), skip: 3, progress: 800, ageSeconds: 5);
        var late = await api.BodyAsync(NewEvent(), skip: 3, progress: 200, ageSeconds: 90);

        // The newer observation arrives first; the older one is received afterwards.
        Assert.Equal(HttpStatusCode.Created, (await api.PostAsync(newer)).StatusCode);
        api.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.Created, (await api.PostAsync(late)).StatusCode);

        var rows = await api.RowsAsync(journeyId: journey);
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].ObservedAt < rows[1].ObservedAt);
        Assert.True(rows[0].ReceivedAt > rows[1].ReceivedAt, "the older observation was received last");

        Observation? current = null;
        await api.WithDbAsync(async db => current = await ObservationIngestion.CurrentAsync(db, journey, CancellationToken.None));
        Assert.Equal(800, current!.ProgressPermille);
    }

    [RequiresDatabaseFact]
    public async Task Migrating_again_keeps_every_stored_observation()
    {
        var (journey, _) = await api.JourneyAsync(skip: 4);
        await api.PostAsync(await api.BodyAsync(NewEvent(), skip: 4, progress: 100, ageSeconds: 30));
        await api.PostAsync(await api.BodyAsync(NewEvent(), skip: 4, progress: 600, ageSeconds: 10));
        var before = await api.RowsAsync(journeyId: journey);

        await api.MigrateAndSeedAsync();
        await api.MigrateAndSeedAsync();

        var after = await api.RowsAsync(journeyId: journey);
        Assert.Equal(2, before.Count);
        Assert.Equal(before.Select(r => r.Id), after.Select(r => r.Id));
        Assert.Equal(before.Select(r => r.ReceivedAt), after.Select(r => r.ReceivedAt));
    }
}

// The same route on a host that has no service key configured: it must refuse everything.
public class ObservationsWithoutServiceKeyTests(TimetableApi api) : IClassFixture<TimetableApi>
{
    [RequiresDatabaseFact]
    public async Task With_no_key_configured_every_write_is_refused()
    {
        foreach (var key in new[] { null, "", "anything" })
        {
            var request = new HttpRequestMessage(HttpMethod.Post, ObservationApi.Path)
            {
                Content = JsonContent.Create(new { sourceEventId = "x" }),
            };
            if (key is not null)
            {
                request.Headers.Add(ServiceKeyFilter.Header, key);
            }

            var response = await api.Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
