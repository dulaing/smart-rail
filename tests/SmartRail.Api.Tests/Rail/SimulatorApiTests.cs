using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Tests.Rail;

// The simulator driven one tick at a time with a hand-moved clock, so every test is exact and
// none sleeps. Needs a disposable PostgreSQL server.
//   SIM-1001: Gampaha 07:35 -> Ragama 07:50/07:51 -> Maradana 08:20/08:22 -> Fort 08:30
//   SIM-1003: Gampaha 07:20 -> Ragama 07:35/07:36 -> Fort 08:15
// Each test uses its own service date so one test's reports never reach another's journeys.
public class SimulatorApiTests(ObservationApi api) : IClassFixture<ObservationApi>
{
    private SimulatorControl Control => api.Factory.Services.GetRequiredService<SimulatorControl>();

    private static DateTimeOffset At(int day, int hour, int minute, int second = 0) =>
        new(TimetableApi.SeedStart.AddDays(day).ToDateTime(new TimeOnly(hour, minute, second)), ColomboTime.Offset);

    private void ClockAt(int day, int hour, int minute, int second = 0) => api.Clock.Set(At(day, hour, minute, second));

    // A new ticker with no memory: the same as the API having been restarted.
    private SimulatorTicker NewTicker(int delaySeconds = 0) =>
        new(Control, new SimulatorOptions(true, delaySeconds), api.Clock, NullLogger<SimulatorTicker>.Instance);

    private async Task<TickSummary> TickAsync(SimulatorTicker ticker)
    {
        TickSummary? summary = null;
        await api.WithDbAsync(async db => summary = await ticker.TickAsync(db, CancellationToken.None));
        return summary!;
    }

    private async Task<Guid> JourneyAsync(string trainNumber, int day)
    {
        var id = Guid.Empty;
        await api.WithDbAsync(async db => id = await db.Journeys
            .Where(j => j.Pattern.TrainNumber == trainNumber && j.ServiceDate == TimetableApi.SeedStart.AddDays(day))
            .Select(j => j.Id).SingleAsync());
        return id;
    }

    private async Task<List<PatternStop>> StopsAsync(Guid journeyId)
    {
        var stops = new List<PatternStop>();
        await api.WithDbAsync(async db => stops = await db.Journeys.Where(j => j.Id == journeyId)
            .SelectMany(j => j.Pattern.Stops).OrderBy(s => s.Sequence).AsNoTracking().ToListAsync());
        return stops;
    }

    private async Task<JsonElement> RunningAsync(Guid journeyId) =>
        await api.Client.GetFromJsonAsync<JsonElement>($"/api/journeys/{journeyId}/running");

    private Task<HttpResponseMessage> CommandAsync(string journeyId, string action, string? key = ObservationApi.Key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/rail/simulator/journeys/{journeyId}/{action}");
        if (key is not null)
        {
            request.Headers.Add(ServiceKeyFilter.Header, key);
        }

        return api.Client.SendAsync(request);
    }

    [RequiresDatabaseFact]
    public async Task Successive_ticks_add_one_report_per_running_train_and_repeats_and_restarts_add_nothing()
    {
        var sim1001 = await JourneyAsync("SIM-1001", 0);
        var sim1003 = await JourneyAsync("SIM-1003", 0);
        ClockAt(0, 7, 40, 10);   // inside the 07:40:00 window

        var first = await TickAsync(NewTicker());
        // SIM-1002 has not left yet and the others are not running.
        Assert.Equal(new TickSummary(Created: 2, Repeated: 0, Paused: 0, Rejected: 0), first);

        // The same window again, then again after a "restart": the same event IDs are recognised.
        var repeat = await TickAsync(NewTicker());
        var afterRestart = await TickAsync(NewTicker());
        Assert.Equal(new TickSummary(0, 2, 0, 0), repeat);
        Assert.Equal(new TickSummary(0, 2, 0, 0), afterRestart);
        var rows = await api.RowsAsync(journeyId: sim1003);
        var only = Assert.Single(rows);
        Assert.Equal(SimulatorPlan.EventId(sim1003, At(0, 7, 40)), only.SourceEventId);
        Assert.Equal(At(0, 7, 40), only.ObservedAt);

        // The next window is a new observation 30 seconds later, further along the line.
        ClockAt(0, 7, 40, 40);
        Assert.Equal(new TickSummary(2, 0, 0, 0), await TickAsync(NewTicker()));
        rows = await api.RowsAsync(journeyId: sim1003);
        Assert.Equal(2, rows.Count);
        Assert.Equal(TimeSpan.FromSeconds(30), rows[1].ObservedAt - rows[0].ObservedAt);
        Assert.True(rows[1].ProgressPermille > rows[0].ProgressPermille);
        Assert.Equal(2, (await api.RowsAsync(journeyId: sim1001)).Count);
    }

    [RequiresDatabaseFact]
    public async Task A_train_waits_through_its_dwell_and_only_then_moves_on_to_the_next_segment()
    {
        var journey = await JourneyAsync("SIM-1003", 1);
        var stops = await StopsAsync(journey);
        var ticker = NewTicker();

        foreach (var (minute, second) in new[] { (34, 30), (35, 0), (35, 30) })
        {
            ClockAt(1, 7, minute, second);
            await TickAsync(ticker);
        }

        // Mid-dwell the guest read agrees: the train is at Ragama and leaves at its scheduled time.
        var body = await RunningAsync(journey);
        Assert.Equal("CURRENT", body.GetProperty("freshness").GetString());
        Assert.Equal(1000, body.GetProperty("currentPosition").GetProperty("progressPermille").GetInt32());
        Assert.Equal(At(1, 7, 36), body.GetProperty("stops")[1].GetProperty("estimatedDeparture").GetDateTimeOffset());

        // At the scheduled departure it leaves: the outbound segment starts at 0.
        ClockAt(1, 7, 36, 0);
        await TickAsync(ticker);

        var rows = await api.RowsAsync(journeyId: journey);
        Assert.Equal([966, 1000, 1000, 0], rows.Select(r => r.ProgressPermille));
        Assert.All(rows.Take(3), r => Assert.Equal((stops[0].Id, stops[1].Id), (r.FromStopId, r.ToStopId)));
        Assert.Equal((stops[1].Id, stops[2].Id), (rows[3].FromStopId, rows[3].ToStopId));
    }

    [RequiresDatabaseFact]
    public async Task Pausing_one_journey_lets_another_continue_then_it_goes_unknown_and_recovers_on_resume()
    {
        var paused = await JourneyAsync("SIM-1001", 2);
        var running = await JourneyAsync("SIM-1003", 2);
        var ticker = NewTicker();
        ClockAt(2, 7, 40);
        Assert.Equal(new TickSummary(2, 0, 0, 0), await TickAsync(ticker));

        Assert.Equal(HttpStatusCode.OK, (await CommandAsync(paused.ToString(), "pause")).StatusCode);

        // Four more minutes pass, one tick every 30 seconds.
        for (var tick = 1; tick <= 8; tick++)
        {
            api.Clock.Advance(TimeSpan.FromSeconds(30));
            var summary = await TickAsync(ticker);
            Assert.Equal(new TickSummary(1, 0, 1, 0), summary);

            var seconds = tick * 30;
            var expected = seconds < 180 ? "CURRENT" : seconds < 240 ? "AGEING" : "UNKNOWN";
            var pausedBody = await RunningAsync(paused);
            Assert.True(expected == pausedBody.GetProperty("freshness").GetString(), $"paused journey after {seconds}s");
            Assert.Equal("CURRENT", (await RunningAsync(running)).GetProperty("freshness").GetString());
        }

        // Unknown after four minutes of silence: no position or estimates, but its history stays.
        var unknown = await RunningAsync(paused);
        Assert.Equal(JsonValueKind.Null, unknown.GetProperty("currentPosition").ValueKind);
        Assert.Equal(JsonValueKind.Null, unknown.GetProperty("delaySeconds").ValueKind);
        Assert.Equal(At(2, 7, 40), unknown.GetProperty("lastObservation").GetProperty("observedAt").GetDateTimeOffset());
        Assert.Single(await api.RowsAsync(journeyId: paused));
        Assert.Equal(9, (await api.RowsAsync(journeyId: running)).Count);

        // Resume: the next tick reports where the train is now. Missed ticks are not replayed.
        Assert.Equal(HttpStatusCode.OK, (await CommandAsync(paused.ToString(), "resume")).StatusCode);
        api.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(new TickSummary(2, 0, 0, 0), await TickAsync(ticker));

        var recovered = await RunningAsync(paused);
        Assert.Equal("CURRENT", recovered.GetProperty("freshness").GetString());
        var rows = await api.RowsAsync(journeyId: paused);
        Assert.Equal([At(2, 7, 40), At(2, 7, 44, 30)], rows.Select(r => r.ObservedAt));
    }

    [RequiresDatabaseFact]
    public async Task Pause_and_resume_need_the_key_and_a_real_journey()
    {
        var journey = await JourneyAsync("SIM-1003", 3);

        Assert.Equal(HttpStatusCode.Unauthorized, (await CommandAsync(journey.ToString(), "pause", key: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CommandAsync(journey.ToString(), "resume", key: "wrong")).StatusCode);
        Assert.False(Control.IsPaused(journey));
        Assert.Equal(HttpStatusCode.BadRequest, (await CommandAsync("not-a-guid", "pause")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CommandAsync(Guid.NewGuid().ToString(), "pause")).StatusCode);

        var paused = await CommandAsync(journey.ToString(), "pause");
        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        Assert.True(Control.IsPaused(journey));
        Assert.True((await paused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paused").GetBoolean());
        await CommandAsync(journey.ToString(), "resume");
        Assert.False(Control.IsPaused(journey));
    }

    [RequiresDatabaseFact]
    public async Task The_final_arrival_is_reported_once_even_after_a_restart_and_nothing_follows()
    {
        var journey = await JourneyAsync("SIM-1003", 4);
        var stops = await StopsAsync(journey);

        foreach (var (minute, second) in new[] { (14, 30), (15, 0), (15, 30), (16, 0), (20, 0) })
        {
            ClockAt(4, 8, minute, second);
            await TickAsync(NewTicker());   // a new ticker each time: a restart between every tick
        }

        ClockAt(4, 8, 15, 10);   // inside the arrival window again, after yet another restart
        await TickAsync(NewTicker());

        var rows = await api.RowsAsync(journeyId: journey);
        Assert.Equal(2, rows.Count);
        var arrival = rows[^1];
        Assert.Equal((stops[1].Id, stops[2].Id, 1000), (arrival.FromStopId, arrival.ToStopId, arrival.ProgressPermille));
        Assert.Equal(At(4, 8, 15), arrival.ObservedAt);
    }

    [RequiresDatabaseFact]
    public async Task A_demo_delay_postpones_the_first_report_and_is_carried_into_the_estimates()
    {
        var journey = await JourneyAsync("SIM-1003", 5);
        var ticker = NewTicker(delaySeconds: 90);

        ClockAt(5, 7, 21, 0);   // on the timetable this is 07:19:30, before departure
        Assert.Equal(new TickSummary(0, 0, 0, 0), await TickAsync(ticker));
        Assert.Empty(await api.RowsAsync(journeyId: journey));

        ClockAt(5, 7, 21, 30);  // 07:20:00 on the timetable: the delayed train leaves now
        Assert.Equal(new TickSummary(1, 0, 0, 0), await TickAsync(ticker));
        var row = Assert.Single(await api.RowsAsync(journeyId: journey));
        Assert.Equal((0, 90), (row.ProgressPermille, row.ReportedDelaySeconds));

        var body = await RunningAsync(journey);
        Assert.Equal(90, body.GetProperty("delaySeconds").GetInt32());
        Assert.Equal(At(5, 7, 36, 30), body.GetProperty("stops")[1].GetProperty("estimatedArrival").GetDateTimeOffset());
    }

    [RequiresDatabaseFact]
    public async Task The_pause_commands_do_not_exist_outside_Development()
    {
        var journey = await JourneyAsync("SIM-1003", 6);
        using var production = api.Factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        using var client = production.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/rail/simulator/journeys/{journey}/pause");
        request.Headers.Add(ServiceKeyFilter.Header, ObservationApi.Key);

        var response = await client.SendAsync(request);

        // The route is not mapped at all. (An unmatched URL gets the app's deny-by-default 401,
        // not a 404, but it is never a success and nothing ran.)
        var routes = production.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>().Select(e => e.RoutePattern.RawText).ToList();
        Assert.DoesNotContain(routes, r => r!.Contains("simulator"));
        Assert.Contains(routes, r => r == "/internal/rail/observations");   // other routes are unaffected
        Assert.False(response.IsSuccessStatusCode);
        Assert.False(Control.IsPaused(journey));
    }
}
