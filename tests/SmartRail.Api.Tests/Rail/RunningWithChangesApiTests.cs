using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Tests.Rail;

// The guest running read with confirmed staff changes applied, on a disposable PostgreSQL and a
// hand-moved clock. SIM-1003 runs daily: Gampaha 07:20 -> Ragama 07:35/07:36 -> Fort 08:15.
public class RunningWithChangesApiTests(ObservationApi api) : IClassFixture<ObservationApi>
{
    private sealed record SimJourney(Guid Id, DateOnly ServiceDate, PatternStop[] Stops)
    {
        public DateTimeOffset Departure => ColomboTime.StopInstants(ServiceDate, Stops[0]).Departure!.Value;

        public DateTimeOffset ArrivalAt(int index) => ColomboTime.StopInstants(ServiceDate, Stops[index]).Arrival!.Value;
    }

    private Guid publisher;

    // Each test uses its own service date, so its changes never touch another test's journey.
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

    private async Task EnsurePublisherAsync()
    {
        if (publisher != Guid.Empty)
        {
            return;
        }

        publisher = Guid.NewGuid();
        await api.WithDbAsync(async db =>
        {
            db.Accounts.Add(new Account
            {
                Id = publisher, Issuer = "https://keycloak.test/realms/smartrail", Subject = $"running-controller-{publisher:N}",
                DisplayName = "Controller", Status = AccountStatus.Active,
            });
            await db.SaveChangesAsync();
        });
    }

    // Records a confirmed change straight in history, as a confirmation would have.
    private async Task<Guid> ConfirmAsync(
        SimJourney journey, ChangeDetails details, ChangeAction action = ChangeAction.Publish, Guid? supersedes = null)
    {
        await EnsurePublisherAsync();
        var id = Guid.NewGuid();
        await api.WithDbAsync(async db =>
        {
            // Versions are per journey; count this journey's rows so each starts at 1.
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

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static List<JsonElement> StopsOf(JsonElement body) => body.GetProperty("stops").EnumerateArray().ToList();

    private static bool IsNull(JsonElement element, string name) => element.GetProperty(name).ValueKind == JsonValueKind.Null;

    private async Task ObserveAsync(SimJourney journey, int progress, DateTimeOffset observedAt, int delay = 0)
    {
        var response = await api.PostAsync(new
        {
            sourceEventId = $"evt-{Guid.NewGuid():N}",
            journeyId = journey.Id,
            fromStopId = journey.Stops[0].Id,
            toStopId = journey.Stops[1].Id,
            progressPermille = progress,
            reportedDelaySeconds = delay,
            observedAt = observedAt.ToString("O"),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<SimJourney> JourneyUnderObservationAsync(int day)
    {
        var journey = await JourneyOnDayAsync(day);
        api.Clock.Set(journey.Departure);
        await ObserveAsync(journey, 100, journey.Departure);
        return journey;
    }

    [RequiresDatabaseFact]
    public async Task A_confirmed_delay_and_platform_change_the_answer_and_its_etag()
    {
        var journey = await JourneyUnderObservationAsync(0);
        var before = await GetAsync(journey.Id);
        var beforeBody = await BodyOf(before);
        Assert.Equal("AS_SCHEDULED", beforeBody.GetProperty("availability").GetString());
        Assert.True(IsNull(beforeBody, "confirmedDelayMinutes"));
        Assert.Equal(journey.ArrivalAt(1), StopsOf(beforeBody)[1].GetProperty("estimatedArrival").GetDateTimeOffset());
        Assert.Equal(HttpStatusCode.NotModified, (await GetAsync(journey.Id, ETagOf(before))).StatusCode);

        await ConfirmAsync(journey, ChangeDetails.Delay(10));
        await ConfirmAsync(journey, ChangeDetails.Platform(journey.Stops[1].Id, "5"));
        var after = await GetAsync(journey.Id, ETagOf(before));

        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.NotEqual(ETagOf(before), ETagOf(after));
        var body = await BodyOf(after);
        var ragama = StopsOf(body)[1];
        Assert.Equal(10, body.GetProperty("confirmedDelayMinutes").GetInt32());
        Assert.Equal(600, body.GetProperty("delaySeconds").GetInt32());
        Assert.Equal(journey.ArrivalAt(1), ragama.GetProperty("scheduledArrival").GetDateTimeOffset());
        Assert.Equal(journey.ArrivalAt(1).AddMinutes(10), ragama.GetProperty("estimatedArrival").GetDateTimeOffset());
        Assert.Equal("2", ragama.GetProperty("plannedPlatform").GetString());
        Assert.Equal("5", ragama.GetProperty("confirmedPlatform").GetString());
        Assert.Equal("5", ragama.GetProperty("platform").GetString());
        Assert.Equal(journey.Departure, ragama.GetProperty("confirmedPlatformAt").GetDateTimeOffset());
        Assert.Equal(HttpStatusCode.NotModified, (await GetAsync(journey.Id, ETagOf(after))).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task A_confirmed_platform_stays_visible_when_the_observation_becomes_unknown()
    {
        var journey = await JourneyUnderObservationAsync(1);
        await ConfirmAsync(journey, ChangeDetails.Platform(journey.Stops[1].Id, "5"));
        await ConfirmAsync(journey, ChangeDetails.Delay(10));
        var fresh = await BodyOf(await GetAsync(journey.Id));
        Assert.Equal("CURRENT", fresh.GetProperty("freshness").GetString());

        api.Clock.Set(journey.Departure.AddSeconds(240));
        var unknown = await BodyOf(await GetAsync(journey.Id));
        var ragama = StopsOf(unknown)[1];

        Assert.Equal("UNKNOWN", unknown.GetProperty("freshness").GetString());
        Assert.True(IsNull(unknown, "currentPosition"));
        Assert.True(IsNull(unknown, "delaySeconds"));
        Assert.All(StopsOf(unknown), s => Assert.True(IsNull(s, "estimatedArrival") && IsNull(s, "estimatedDeparture")));
        Assert.Equal("5", ragama.GetProperty("platform").GetString());
        Assert.Equal(StopsOf(fresh)[1].GetProperty("confirmedPlatformAt").GetDateTimeOffset(),
            ragama.GetProperty("confirmedPlatformAt").GetDateTimeOffset());
        Assert.Equal(journey.ArrivalAt(1), ragama.GetProperty("scheduledArrival").GetDateTimeOffset());
        Assert.Equal(10, unknown.GetProperty("confirmedDelayMinutes").GetInt32());
    }

    [RequiresDatabaseFact]
    public async Task A_cancellation_removes_estimates_and_withdrawing_it_restores_them()
    {
        var journey = await JourneyUnderObservationAsync(2);
        var cancel = await ConfirmAsync(journey, ChangeDetails.Cancel());

        var cancelled = await BodyOf(await GetAsync(journey.Id));

        Assert.Equal("CANCELLED", cancelled.GetProperty("availability").GetString());
        Assert.True(IsNull(cancelled, "currentPosition"));
        Assert.Equal(journey.Departure, cancelled.GetProperty("lastObservation").GetProperty("observedAt").GetDateTimeOffset());
        Assert.All(StopsOf(cancelled), s =>
        {
            Assert.False(s.GetProperty("served").GetBoolean());
            Assert.True(IsNull(s, "estimatedArrival") && IsNull(s, "estimatedDeparture"));
        });
        Assert.Equal(journey.ArrivalAt(2), StopsOf(cancelled)[2].GetProperty("scheduledArrival").GetDateTimeOffset());

        await ConfirmAsync(journey, new ChangeDetails(ChangeKind.Cancel), ChangeAction.Withdraw, cancel);
        var restored = await BodyOf(await GetAsync(journey.Id));
        Assert.Equal("AS_SCHEDULED", restored.GetProperty("availability").GetString());
        Assert.Equal(journey.ArrivalAt(2), StopsOf(restored)[2].GetProperty("estimatedArrival").GetDateTimeOffset());
    }

    [RequiresDatabaseFact]
    public async Task An_early_end_keeps_the_final_arrival_and_withdrawal_restores_only_fresh_predictions()
    {
        var journey = await JourneyUnderObservationAsync(3);
        var ending = await ConfirmAsync(journey, ChangeDetails.EarlyEnd(journey.Stops[1].Id));

        var ended = await BodyOf(await GetAsync(journey.Id));
        var stops = StopsOf(ended);

        Assert.Equal("ENDS_EARLY", ended.GetProperty("availability").GetString());
        Assert.Equal(journey.Stops[1].Id, ended.GetProperty("finalStopId").GetGuid());
        Assert.Equal("CURRENT", ended.GetProperty("freshness").GetString());
        Assert.True(stops[0].GetProperty("served").GetBoolean());
        Assert.True(stops[1].GetProperty("served").GetBoolean());
        Assert.Equal(journey.ArrivalAt(1), stops[1].GetProperty("estimatedArrival").GetDateTimeOffset());
        Assert.False(stops[2].GetProperty("served").GetBoolean());
        Assert.True(IsNull(stops[2], "estimatedArrival"));
        Assert.Equal(journey.ArrivalAt(2), stops[2].GetProperty("scheduledArrival").GetDateTimeOffset());

        // Withdrawn while the report is still fresh: the stop is predicted again.
        await ConfirmAsync(journey, new ChangeDetails(ChangeKind.EarlyEnd), ChangeAction.Withdraw, ending);
        var restored = StopsOf(await BodyOf(await GetAsync(journey.Id)));
        Assert.True(restored[2].GetProperty("served").GetBoolean());
        Assert.Equal(journey.ArrivalAt(2), restored[2].GetProperty("estimatedArrival").GetDateTimeOffset());
    }

    [RequiresDatabaseFact]
    public async Task Withdrawing_an_early_end_does_not_revive_a_prediction_from_stale_evidence()
    {
        var journey = await JourneyUnderObservationAsync(4);
        var ending = await ConfirmAsync(journey, ChangeDetails.EarlyEnd(journey.Stops[1].Id));

        api.Clock.Set(journey.Departure.AddSeconds(300));
        await ConfirmAsync(journey, new ChangeDetails(ChangeKind.EarlyEnd), ChangeAction.Withdraw, ending);
        var stale = await BodyOf(await GetAsync(journey.Id));

        Assert.Equal("UNKNOWN", stale.GetProperty("freshness").GetString());
        Assert.Equal("AS_SCHEDULED", stale.GetProperty("availability").GetString());
        Assert.All(StopsOf(stale), s =>
        {
            Assert.True(s.GetProperty("served").GetBoolean());
            Assert.True(IsNull(s, "estimatedArrival"));
        });

        // A new report makes the prediction valid again.
        await ObserveAsync(journey, 300, api.Clock.Now);
        var recovered = StopsOf(await BodyOf(await GetAsync(journey.Id)));
        Assert.NotEqual(JsonValueKind.Null, recovered[2].GetProperty("estimatedArrival").ValueKind);
    }
}
