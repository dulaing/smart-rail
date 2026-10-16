using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Tests.Rail;

// The guest running read, driven by a hand-moved clock. Needs a disposable PostgreSQL server.
// SIM-1003 runs daily: Gampaha 07:20 -> Ragama 07:35/07:36 -> Fort 08:15 (Colombo time).
public class RunningApiTests(ObservationApi api) : IClassFixture<ObservationApi>
{
    private sealed record SimJourney(Guid Id, DateOnly ServiceDate, PatternStop[] Stops)
    {
        public DateTimeOffset Departure => ColomboTime.StopInstants(ServiceDate, Stops[0]).Departure!.Value;

        public DateTimeOffset ArrivalAt(int stopIndex) => ColomboTime.StopInstants(ServiceDate, Stops[stopIndex]).Arrival!.Value;
    }

    // Each test uses its own service date, so one test's observations never touch another's journey.
    private async Task<SimJourney> JourneyOnDayAsync(int day)
    {
        SimJourney? found = null;
        await api.WithDbAsync(async db =>
        {
            var journey = await db.Journeys.AsNoTracking()
                .Include(j => j.Pattern).ThenInclude(p => p.Stops)
                .SingleAsync(j => j.Pattern.TrainNumber == "SIM-1003" && j.ServiceDate == TimetableApi.SeedStart.AddDays(day));
            found = new SimJourney(journey.Id, journey.ServiceDate, journey.Pattern.Stops.OrderBy(s => s.Sequence).ToArray());
        });
        return found!;
    }

    private async Task<HttpResponseMessage> GetAsync(Guid journeyId, string? etag = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/journeys/{journeyId}/running");
        if (etag is not null)
        {
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag));
        }

        return await api.Client.SendAsync(request);
    }

    private static string ETagOf(HttpResponseMessage response) => response.Headers.ETag!.ToString();

    private static DateTimeOffset ServerTimeOf(HttpResponseMessage response) =>
        DateTimeOffset.Parse(response.Headers.GetValues(RunningEndpoints.ServerTimeHeader).Single());

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    // Reports the train on the segment between two stops, observed at `observedAt`.
    private async Task PostObservationAsync(
        SimJourney journey, int fromIndex, int progress, DateTimeOffset observedAt, int delay = 0)
    {
        var response = await api.PostAsync(new
        {
            sourceEventId = $"evt-{Guid.NewGuid():N}",
            journeyId = journey.Id,
            fromStopId = journey.Stops[fromIndex].Id,
            toStopId = journey.Stops[fromIndex + 1].Id,
            progressPermille = progress,
            reportedDelaySeconds = delay,
            observedAt = observedAt.ToString("O"),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static List<JsonElement> StopsOf(JsonElement body) => body.GetProperty("stops").EnumerateArray().ToList();

    private static bool AllEstimatesAreNull(JsonElement body) => StopsOf(body).All(s =>
        s.GetProperty("estimatedArrival").ValueKind == JsonValueKind.Null
        && s.GetProperty("estimatedDeparture").ValueKind == JsonValueKind.Null);

    [RequiresDatabaseFact]
    public async Task A_malformed_id_is_400_and_an_unknown_journey_is_404()
    {
        var malformed = await api.Client.GetAsync("/api/journeys/not-a-guid/running");
        var unknown = await api.Client.GetAsync($"/api/journeys/{Guid.NewGuid()}/running");

        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task A_journey_with_no_observation_is_unknown_with_its_schedule_and_labels_intact()
    {
        var journey = await JourneyOnDayAsync(0);
        api.Clock.Set(journey.Departure);

        var response = await GetAsync(journey.Id);   // anonymous: no sign-in is sent

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(api.Clock.Now, ServerTimeOf(response));
        var body = await BodyOf(response);
        Assert.Equal("UNKNOWN", body.GetProperty("freshness").GetString());
        Assert.Equal("SmartRail simulation", body.GetProperty("runningLabel").GetString());
        Assert.Equal(180, body.GetProperty("thresholds").GetProperty("ageingAfterSeconds").GetInt32());
        Assert.Equal(240, body.GetProperty("thresholds").GetProperty("unknownAfterSeconds").GetInt32());
        Assert.Equal("FICTIONAL_DEMO", body.GetProperty("timetable").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("lastObservation").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("currentPosition").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("delaySeconds").ValueKind);
        Assert.True(AllEstimatesAreNull(body));

        var stops = StopsOf(body);
        Assert.Equal(journey.Stops.Select(s => s.Id), stops.Select(s => s.GetProperty("stopId").GetGuid()));
        Assert.Equal(["1", "2", "4"], stops.Select(s => s.GetProperty("plannedPlatform").GetString()));
        Assert.Equal(journey.Departure, stops[0].GetProperty("scheduledDeparture").GetDateTimeOffset());
        Assert.Equal(journey.ArrivalAt(2), stops[2].GetProperty("scheduledArrival").GetDateTimeOffset());

        // Nothing it says changes, so asking again with its ETag is a 304.
        api.Clock.Advance(TimeSpan.FromMinutes(10));
        var again = await GetAsync(journey.Id, ETagOf(response));
        Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
        Assert.Equal(api.Clock.Now, ServerTimeOf(again));
    }

    [RequiresDatabaseFact]
    public async Task Evidence_goes_current_then_ageing_then_unknown_and_recovers_with_a_fresh_report()
    {
        var journey = await JourneyOnDayAsync(1);
        var seen = journey.Departure;
        api.Clock.Set(seen);
        await PostObservationAsync(journey, fromIndex: 0, progress: 100, observedAt: seen);

        // CURRENT: position, delay and estimates are present.
        var current = await GetAsync(journey.Id);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var currentBody = await BodyOf(current);
        Assert.Equal("CURRENT", currentBody.GetProperty("freshness").GetString());
        Assert.Equal(100, currentBody.GetProperty("currentPosition").GetProperty("progressPermille").GetInt32());
        Assert.Equal(0, currentBody.GetProperty("delaySeconds").GetInt32());
        Assert.Equal(seen, currentBody.GetProperty("lastObservation").GetProperty("observedAt").GetDateTimeOffset());
        Assert.Equal(journey.Stops[0].StationId,
            currentBody.GetProperty("lastObservation").GetProperty("lastKnownStationId").GetGuid());
        Assert.Equal(journey.ArrivalAt(1), StopsOf(currentBody)[1].GetProperty("estimatedArrival").GetDateTimeOffset());
        var currentTag = ETagOf(current);

        // Time passing alone does not change a current answer, right up to the threshold.
        foreach (var seconds in new[] { 100, 179 })
        {
            api.Clock.Set(seen.AddSeconds(seconds));
            var unchanged = await GetAsync(journey.Id, currentTag);
            Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
            Assert.Equal(api.Clock.Now, ServerTimeOf(unchanged));
        }

        // 180 s: AGEING. The state changed, so a full answer with a new ETag. Estimates remain.
        api.Clock.Set(seen.AddSeconds(180));
        var ageing = await GetAsync(journey.Id, currentTag);
        Assert.Equal(HttpStatusCode.OK, ageing.StatusCode);
        var ageingBody = await BodyOf(ageing);
        Assert.Equal("AGEING", ageingBody.GetProperty("freshness").GetString());
        Assert.Equal(100, ageingBody.GetProperty("currentPosition").GetProperty("progressPermille").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, StopsOf(ageingBody)[1].GetProperty("estimatedArrival").ValueKind);
        var ageingTag = ETagOf(ageing);
        Assert.NotEqual(currentTag, ageingTag);

        api.Clock.Set(seen.AddSeconds(239));
        Assert.Equal(HttpStatusCode.NotModified, (await GetAsync(journey.Id, ageingTag)).StatusCode);

        // 240 s: UNKNOWN. No position, delay or estimates, but the report and schedule remain.
        api.Clock.Set(seen.AddSeconds(240));
        var unknown = await GetAsync(journey.Id, ageingTag);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        var unknownBody = await BodyOf(unknown);
        Assert.Equal("UNKNOWN", unknownBody.GetProperty("freshness").GetString());
        Assert.Equal(JsonValueKind.Null, unknownBody.GetProperty("currentPosition").ValueKind);
        Assert.Equal(JsonValueKind.Null, unknownBody.GetProperty("delaySeconds").ValueKind);
        Assert.True(AllEstimatesAreNull(unknownBody));
        Assert.Equal(seen, unknownBody.GetProperty("lastObservation").GetProperty("observedAt").GetDateTimeOffset());
        Assert.Equal(journey.ArrivalAt(1), StopsOf(unknownBody)[1].GetProperty("scheduledArrival").GetDateTimeOffset());
        Assert.Equal("2", StopsOf(unknownBody)[1].GetProperty("plannedPlatform").GetString());
        var unknownTag = ETagOf(unknown);
        Assert.NotEqual(ageingTag, unknownTag);
        Assert.Single(await api.RowsAsync(journeyId: journey.Id));   // history was kept

        // Staying unknown changes nothing, however long it lasts. Polling or retrying cannot revive it.
        api.Clock.Set(seen.AddSeconds(600));
        Assert.Equal(HttpStatusCode.NotModified, (await GetAsync(journey.Id, unknownTag)).StatusCode);
        Assert.Equal("UNKNOWN", (await BodyOf(await GetAsync(journey.Id))).GetProperty("freshness").GetString());

        // A fresh observation recovers it.
        await PostObservationAsync(journey, fromIndex: 0, progress: 800, observedAt: api.Clock.Now, delay: 60);
        var recovered = await GetAsync(journey.Id, unknownTag);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        var recoveredBody = await BodyOf(recovered);
        Assert.Equal("CURRENT", recoveredBody.GetProperty("freshness").GetString());
        Assert.Equal(60, recoveredBody.GetProperty("delaySeconds").GetInt32());
        Assert.Equal(journey.ArrivalAt(1).AddSeconds(60), StopsOf(recoveredBody)[1].GetProperty("estimatedArrival").GetDateTimeOffset());
        var recoveredTag = ETagOf(recovered);
        Assert.NotEqual(unknownTag, recoveredTag);

        // A late report older than the current one is kept as history but changes nothing shown.
        await PostObservationAsync(journey, fromIndex: 0, progress: 300, observedAt: api.Clock.Now.AddSeconds(-90));
        Assert.Equal(3, (await api.RowsAsync(journeyId: journey.Id)).Count);
        Assert.Equal(HttpStatusCode.NotModified, (await GetAsync(journey.Id, recoveredTag)).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task A_new_report_changes_the_answer_even_when_no_time_has_passed()
    {
        var journey = await JourneyOnDayAsync(2);
        api.Clock.Set(journey.Departure);
        await PostObservationAsync(journey, fromIndex: 0, progress: 100, observedAt: api.Clock.Now);
        var first = await GetAsync(journey.Id);

        api.Clock.Advance(TimeSpan.FromSeconds(30));
        await PostObservationAsync(journey, fromIndex: 0, progress: 600, observedAt: api.Clock.Now);
        var second = await GetAsync(journey.Id, ETagOf(first));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(600, (await BodyOf(second)).GetProperty("currentPosition").GetProperty("progressPermille").GetInt32());
        Assert.NotEqual(ETagOf(first), ETagOf(second));
    }

    [RequiresDatabaseFact]
    public async Task An_overdue_stop_changes_the_estimate_with_time_so_the_answer_is_not_a_304()
    {
        var journey = await JourneyOnDayAsync(3);
        // Ragama was due at 07:35; the train is still reported before it 30 seconds later.
        api.Clock.Set(journey.ArrivalAt(1).AddSeconds(30));
        await PostObservationAsync(journey, fromIndex: 0, progress: 900, observedAt: api.Clock.Now);

        var first = await GetAsync(journey.Id);
        var firstBody = await BodyOf(first);
        Assert.Equal(30, firstBody.GetProperty("delaySeconds").GetInt32());
        Assert.Equal(api.Clock.Now, StopsOf(firstBody)[1].GetProperty("estimatedArrival").GetDateTimeOffset());

        api.Clock.Advance(TimeSpan.FromSeconds(10));
        var later = await GetAsync(journey.Id, ETagOf(first));

        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        var laterBody = await BodyOf(later);
        Assert.Equal(40, laterBody.GetProperty("delaySeconds").GetInt32());
        Assert.Equal(api.Clock.Now, StopsOf(laterBody)[1].GetProperty("estimatedArrival").GetDateTimeOffset());
        Assert.Equal(journey.ArrivalAt(2).AddSeconds(40), StopsOf(laterBody)[2].GetProperty("estimatedArrival").GetDateTimeOffset());
        Assert.NotEqual(ETagOf(first), ETagOf(later));
    }
}

// The response builder alone: no database and no host.
public class RunningResponseTests
{
    [Fact]
    public void A_published_timetable_keeps_its_own_label_but_the_running_label_stays_simulated()
    {
        var pattern = new ServicePattern
        {
            Id = Guid.NewGuid(),
            TrainNumber = "1",
            SourceKind = SourceKind.Published,
            SourceUrl = "https://example.test/timetable",
            SourceCheckedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Stops = [],
        };
        var journey = new ScheduledJourney
        {
            Id = Guid.NewGuid(), PatternId = pattern.Id, ServiceDate = new DateOnly(2026, 10, 14), Pattern = pattern,
        };

        var state = RunningCalculator.Calculate(journey, null, new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero));
        var response = RunningResponses.Build(journey, state, null);

        Assert.Equal("PUBLISHED", response.Timetable.Kind);
        Assert.Equal("Published timetable", response.Timetable.Label);
        Assert.Equal("https://example.test/timetable", response.Timetable.Url);
        Assert.Equal("SmartRail simulation", response.RunningLabel);
        Assert.Equal("UNKNOWN", response.Freshness);
    }
}
