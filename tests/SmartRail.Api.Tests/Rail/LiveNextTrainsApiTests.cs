using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Tests.Rail;

// GET /api/live-next-trains on a disposable PostgreSQL and a hand-moved clock. The window reaches
// two service dates back and one ahead, so each test that changes a journey uses its own date.
// SIM-1003 runs daily: Gampaha 07:20 -> Ragama 07:35/07:36 -> Fort 08:15.
public class LiveNextTrainsApiTests(ObservationApi api) : IClassFixture<ObservationApi>
{
    private Guid publisher;

    private static DateTimeOffset Colombo(DateOnly date, int hour, int minute, int second = 0) =>
        ColomboTime.ToInstant(date, new TimeOnly(hour, minute, second), 0);

    private static DateOnly Day(int offset) => TimetableApi.SeedStart.AddDays(offset);

    private sealed record Sim(Guid Id, PatternStop[] Stops);

    private async Task<Sim> JourneyAsync(string trainNumber, int day)
    {
        Sim? found = null;
        await api.WithDbAsync(async db =>
        {
            var journey = await db.Journeys.AsNoTracking()
                .Include(j => j.Pattern).ThenInclude(p => p.Stops)
                .SingleAsync(j => j.Pattern.TrainNumber == trainNumber && j.ServiceDate == Day(day));
            found = new Sim(journey.Id, journey.Pattern.Stops.OrderBy(s => s.Sequence).ToArray());
        });
        return found!;
    }

    private async Task<Guid> StationAsync(string code)
    {
        var id = Guid.Empty;
        await api.WithDbAsync(async db => id = (await db.Stations.AsNoTracking().SingleAsync(s => s.Code == code)).Id);
        return id;
    }

    private async Task ObserveAsync(Sim journey, int fromIndex, int progress, DateTimeOffset observedAt, int delaySeconds = 0)
    {
        var response = await api.PostAsync(new
        {
            sourceEventId = $"evt-{Guid.NewGuid():N}",
            journeyId = journey.Id,
            fromStopId = journey.Stops[fromIndex].Id,
            toStopId = journey.Stops[fromIndex + 1].Id,
            progressPermille = progress,
            reportedDelaySeconds = delaySeconds,
            observedAt = observedAt.ToString("O"),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // Records a confirmed change straight in history, as a confirmation would have.
    private async Task<Guid> ConfirmAsync(
        Sim journey, ChangeDetails details, ChangeAction action = ChangeAction.Publish, Guid? supersedes = null)
    {
        if (publisher == Guid.Empty)
        {
            publisher = Guid.NewGuid();
            await api.WithDbAsync(async db =>
            {
                db.Accounts.Add(new Account
                {
                    Id = publisher, Issuer = "https://keycloak.test/realms/smartrail", Subject = $"live-controller-{publisher:N}",
                    DisplayName = "Controller", Status = AccountStatus.Active,
                });
                await db.SaveChangesAsync();
            });
        }

        var id = Guid.NewGuid();
        await api.WithDbAsync(async db =>
        {
            var next = await db.ServiceChanges.CountAsync(c => c.JourneyId == journey.Id) + 1;
            db.ServiceChanges.Add(new ServiceChange(id, next, action, details, supersedes)
            {
                JourneyId = journey.Id, PublishedById = publisher, PublishedAt = api.Clock.Now.ToUniversalTime(),
            });
            (await db.JourneyVersions.SingleAsync(v => v.JourneyId == journey.Id)).Version = next;
            await db.SaveChangesAsync();
        });
        return id;
    }

    private async Task<HttpResponseMessage> GetAsync(Guid origin, Guid destination, string? etag = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/live-next-trains?originId={origin}&destinationId={destination}");
        if (etag is not null)
        {
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag));
        }

        return await api.Client.SendAsync(request);
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<JsonElement> Trains(JsonElement body) => body.GetProperty("trains").EnumerateArray().ToList();

    private static List<JsonElement> Notices(JsonElement body) => body.GetProperty("notices").EnumerateArray().ToList();

    private static List<string> Numbers(JsonElement body) =>
        Trains(body).Select(t => t.GetProperty("trainNumber").GetString()!).ToList();

    private static bool IsNull(JsonElement element, string name) => element.GetProperty(name).ValueKind == JsonValueKind.Null;

    private static string ETagOf(HttpResponseMessage response) => response.Headers.ETag!.ToString();

    [RequiresDatabaseFact]
    public async Task A_delayed_train_stays_findable_after_its_scheduled_time_until_it_is_seen_leaving()
    {
        var ragama = await StationAsync("RGM");
        var fort = await StationAsync("FOT");
        var journey = await JourneyAsync("SIM-1003", 0);
        var now = Colombo(Day(0), 7, 40);
        api.Clock.Set(now);
        // Seen between Gampaha and Ragama, ten minutes late; Ragama was due to leave at 07:36.
        await ObserveAsync(journey, 0, 500, now, delaySeconds: 600);

        var live = await BodyOf(await GetAsync(ragama, fort));
        var scheduled = await api.Client.GetFromJsonAsync<JsonElement>(
            $"/api/next-trains?originId={ragama}&destinationId={fort}&at={Uri.EscapeDataString(now.ToString("O"))}");

        Assert.DoesNotContain(scheduled.GetProperty("trains").EnumerateArray(),
            t => t.GetProperty("journeyId").GetGuid() == journey.Id);
        Assert.Equal(["SIM-1003", "SIM-1001", "SIM-1002"], Numbers(live));
        var delayed = Trains(live)[0];
        Assert.Equal(Colombo(Day(0), 7, 36), delayed.GetProperty("originScheduledDeparture").GetDateTimeOffset());
        Assert.Equal(Colombo(Day(0), 7, 46), delayed.GetProperty("originEstimatedDeparture").GetDateTimeOffset());
        Assert.Equal(Colombo(Day(0), 8, 25), delayed.GetProperty("destinationEstimatedArrival").GetDateTimeOffset());
        Assert.Equal("CURRENT", delayed.GetProperty("freshness").GetString());
        Assert.Equal("FICTIONAL_DEMO", delayed.GetProperty("timetable").GetProperty("kind").GetString());
        Assert.Equal("SmartRail simulation", live.GetProperty("runningLabel").GetString());

        // Once it is seen on the way to Fort it has left Ragama and is no longer an option there.
        now = now.AddMinutes(11);
        api.Clock.Set(now);
        await ObserveAsync(journey, 1, 100, now);
        var gone = await BodyOf(await GetAsync(ragama, fort));

        Assert.DoesNotContain(Trains(gone), t => t.GetProperty("journeyId").GetGuid() == journey.Id);
        Assert.Equal(["SIM-1001", "SIM-1002"], Numbers(gone).Take(2));
    }

    [RequiresDatabaseFact]
    public async Task A_cancellation_or_early_end_beyond_the_destination_becomes_a_notice_not_a_choice()
    {
        var gampaha = await StationAsync("GPH");
        var ragama = await StationAsync("RGM");
        var fort = await StationAsync("FOT");
        var journey = await JourneyAsync("SIM-1003", 3);
        api.Clock.Set(Colombo(Day(3), 7, 0));

        var before = await GetAsync(gampaha, fort);
        var beforeBody = await BodyOf(before);
        Assert.Equal(journey.Id, Trains(beforeBody)[0].GetProperty("journeyId").GetGuid());
        Assert.Empty(Notices(beforeBody));

        var cancel = await ConfirmAsync(journey, ChangeDetails.Cancel());
        var cancelledResponse = await GetAsync(gampaha, fort, ETagOf(before));
        var cancelled = await BodyOf(cancelledResponse);

        Assert.NotEqual(ETagOf(before), ETagOf(cancelledResponse));
        Assert.DoesNotContain(Trains(cancelled), t => t.GetProperty("journeyId").GetGuid() == journey.Id);
        var notice = Assert.Single(Notices(cancelled));
        Assert.Equal("CANCELLED", notice.GetProperty("kind").GetString());
        Assert.Equal(journey.Id, notice.GetProperty("journeyId").GetGuid());
        Assert.Equal(Colombo(Day(3), 7, 20), notice.GetProperty("originScheduledDeparture").GetDateTimeOffset());
        Assert.Equal(api.Clock.Now, notice.GetProperty("confirmedAt").GetDateTimeOffset());
        Assert.Equal(1, notice.GetProperty("confirmedVersion").GetInt32());

        // Withdraw it and end the train at Ragama: Fort is unserved, Ragama still is.
        await ConfirmAsync(journey, new ChangeDetails(ChangeKind.Cancel), ChangeAction.Withdraw, cancel);
        await ConfirmAsync(journey, ChangeDetails.EarlyEnd(journey.Stops[1].Id));

        var toFort = await BodyOf(await GetAsync(gampaha, fort));
        var ends = Assert.Single(Notices(toFort));
        Assert.Equal("ENDS_EARLY", ends.GetProperty("kind").GetString());
        Assert.Equal(journey.Stops[1].Id, ends.GetProperty("finalStopId").GetGuid());
        Assert.DoesNotContain(Trains(toFort), t => t.GetProperty("journeyId").GetGuid() == journey.Id);

        var toRagama = await BodyOf(await GetAsync(gampaha, ragama));
        var served = Trains(toRagama).Single(t => t.GetProperty("journeyId").GetGuid() == journey.Id);
        Assert.Equal("ENDS_EARLY", served.GetProperty("availability").GetString());
        Assert.Empty(Notices(toRagama));
    }

    [RequiresDatabaseFact]
    public async Task An_overnight_train_from_the_previous_service_date_is_still_listed_while_overdue()
    {
        var fort = await StationAsync("FOT");
        var gampaha = await StationAsync("GPH");
        // SIM-9001 leaves Fort at 23:45 on the 14th and reaches Gampaha at 00:45 on the 15th.
        api.Clock.Set(Colombo(Day(1), 0, 10));

        var body = await BodyOf(await GetAsync(fort, gampaha));

        var trains = Trains(body);
        Assert.Equal(["SIM-9001", "SIM-3001", "SIM-2001"], Numbers(body));
        Assert.Equal(Day(0).ToString("yyyy-MM-dd"), trains[0].GetProperty("serviceDate").GetString());
        Assert.True(trains[0].GetProperty("overdue").GetBoolean());
        Assert.True(IsNull(trains[0], "originEstimatedDeparture"));
        Assert.Equal("UNKNOWN", trains[0].GetProperty("freshness").GetString());
        Assert.False(trains[1].GetProperty("overdue").GetBoolean());
        Assert.All(trains, t => Assert.False(t.GetProperty("earliestArrival").GetBoolean()));
    }

    [RequiresDatabaseFact]
    public async Task The_read_carries_server_time_and_etags_that_move_only_when_the_picture_moves()
    {
        var ragama = await StationAsync("RGM");
        var fort = await StationAsync("FOT");
        var journey = await JourneyAsync("SIM-1003", 6);
        var seen = Colombo(Day(6), 7, 30);
        api.Clock.Set(seen);
        await ObserveAsync(journey, 0, 500, seen);

        var first = await GetAsync(ragama, fort);
        var firstBody = await BodyOf(first);
        Assert.Equal("2026-10-20T02:00:00.000Z", first.Headers.GetValues(RunningEndpoints.ServerTimeHeader).Single());
        Assert.Equal("CURRENT", Trains(firstBody)[0].GetProperty("freshness").GetString());

        // Time passes but nothing the guest sees has changed: 304, still with the server clock.
        api.Clock.Set(seen.AddSeconds(100));
        var same = await GetAsync(ragama, fort, ETagOf(first));
        Assert.Equal(HttpStatusCode.NotModified, same.StatusCode);
        Assert.Equal(ETagOf(first), ETagOf(same));
        Assert.Equal("2026-10-20T02:01:40.000Z", same.Headers.GetValues(RunningEndpoints.ServerTimeHeader).Single());

        // Crossing 180 s and 240 s changes the answer, so the ETag changes with it.
        api.Clock.Set(seen.AddSeconds(180));
        var ageing = await GetAsync(ragama, fort, ETagOf(first));
        Assert.Equal(HttpStatusCode.OK, ageing.StatusCode);
        Assert.Equal("AGEING", Trains(await BodyOf(ageing))[0].GetProperty("freshness").GetString());

        api.Clock.Set(seen.AddSeconds(240));
        var unknown = await GetAsync(ragama, fort, ETagOf(ageing));
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.NotEqual(ETagOf(ageing), ETagOf(unknown));
        var train = Trains(await BodyOf(unknown))[0];
        Assert.Equal("UNKNOWN", train.GetProperty("freshness").GetString());
        Assert.True(IsNull(train, "originEstimatedDeparture"));
        Assert.True(IsNull(train, "destinationEstimatedArrival"));
        Assert.Equal(seen, train.GetProperty("lastObservedAt").GetDateTimeOffset());
        Assert.Equal(HttpStatusCode.NotModified, (await GetAsync(ragama, fort, ETagOf(unknown))).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task Invalid_unknown_and_empty_requests_answer_like_the_scheduled_read()
    {
        var gampaha = await StationAsync("GPH");
        var peradeniya = await StationAsync("PDN");
        var fort = await StationAsync("FOT");
        api.Clock.Set(Colombo(Day(2), 12, 0));

        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync("/api/live-next-trains")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await api.Client.GetAsync($"/api/live-next-trains?originId=nope&destinationId={fort}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GetAsync(gampaha, gampaha)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(gampaha, Guid.NewGuid())).StatusCode);

        // PDN -> GPH is a known route pair with no train in that direction: success, not an error.
        var empty = await GetAsync(peradeniya, gampaha);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        var body = await empty.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(Trains(body));
        Assert.Empty(Notices(body));
        Assert.True(empty.Headers.Contains(RunningEndpoints.ServerTimeHeader));
    }
}
