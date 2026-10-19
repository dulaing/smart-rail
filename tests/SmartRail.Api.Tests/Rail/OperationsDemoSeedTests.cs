using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Tests.Rail;

public class OperationsDemoSeedTests(ObservationApi api) : IClassFixture<ObservationApi>
{
    private async Task<IReadOnlyList<OperationsDemoSeeder.SeededJourney>> Seed(DateTimeOffset anchor)
    {
        IReadOnlyList<OperationsDemoSeeder.SeededJourney> rows = [];
        await api.WithDbAsync(async db => rows = await OperationsDemoSeeder.SeedAsync(db, api.Clock, anchor));
        return rows;
    }

    private async Task<JsonElement> Running(Guid id) =>
        await api.Client.GetFromJsonAsync<JsonElement>($"/api/journeys/{id}/running");

    [RequiresDatabaseFact]
    public async Task Seed_is_repeatable_preserves_versions_and_simulator_cannot_refresh_the_samples()
    {
        var anchor = DateTimeOffset.Parse("2026-10-18T12:00:00Z");
        api.Clock.Set(anchor);
        var rows = await Seed(anchor);
        var fresh = rows.Single(r => r.TrainNumber == "SIM-OPS-FRESH");
        await api.WithDbAsync(async db =>
        {
            var version = await db.JourneyVersions.SingleAsync(v => v.JourneyId == fresh.JourneyId);
            version.Version = 7;
            await db.SaveChangesAsync();
        });
        Assert.Equal(rows, await Seed(anchor));
        foreach (var row in rows)
        {
            var body = await Running(row.JourneyId);
            var expected = row.TrainNumber.EndsWith("FRESH") ? "CURRENT"
                : row.TrainNumber.EndsWith("AGEING") ? "AGEING" : "UNKNOWN";
            Assert.Equal(expected, body.GetProperty("freshness").GetString());
            Assert.Equal("FICTIONAL_DEMO", body.GetProperty("timetable").GetProperty("kind").GetString());
        }
        await api.WithDbAsync(async db =>
        {
            var ids = rows.Select(r => r.JourneyId).ToArray();
            Assert.Equal(4, await db.Journeys.CountAsync(j => ids.Contains(j.Id)));
            Assert.Equal(3, await db.Observations.CountAsync(o => ids.Contains(o.JourneyId)));
            Assert.Equal(7, (await db.JourneyVersions.SingleAsync(v => v.JourneyId == fresh.JourneyId)).Version);
            Assert.All(await db.Journeys.Where(j => ids.Contains(j.Id)).ToListAsync(), j => Assert.Null(j.DemoSeedKey));
            var before = await db.Observations.Where(o => ids.Contains(o.JourneyId)).Select(o => o.Id).ToListAsync();
            var ticker = new SimulatorTicker(new SimulatorControl(), new SimulatorOptions(true, 0), api.Clock,
                NullLogger<SimulatorTicker>.Instance);
            await ticker.TickAsync(db, default);
            Assert.Equal(before.Order(), (await db.Observations.Where(o => ids.Contains(o.JourneyId))
                .Select(o => o.Id).ToListAsync()).Order());
        });
    }

    [RequiresDatabaseFact]
    public async Task Fresh_sample_returns_304_then_ages_and_expires_without_any_new_write()
    {
        var anchor = DateTimeOffset.Parse("2026-10-19T10:00:00Z");
        api.Clock.Set(anchor);
        var fresh = (await Seed(anchor)).Single(r => r.TrainNumber == "SIM-OPS-FRESH");
        var path = $"/api/journeys/{fresh.JourneyId}/running";
        var first = await api.Client.GetAsync(path);
        api.Clock.Advance(TimeSpan.FromSeconds(30));
        using var conditional = new HttpRequestMessage(HttpMethod.Get, path);
        conditional.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await api.Client.SendAsync(conditional)).StatusCode);
        api.Clock.Set(anchor.AddSeconds(180));
        Assert.Equal("AGEING", (await Running(fresh.JourneyId)).GetProperty("freshness").GetString());
        api.Clock.Set(anchor.AddSeconds(240));
        var expired = await Running(fresh.JourneyId);
        Assert.Equal("UNKNOWN", expired.GetProperty("freshness").GetString());
        Assert.Equal(JsonValueKind.Null, expired.GetProperty("currentPosition").ValueKind);
        Assert.Equal(JsonValueKind.Object, expired.GetProperty("lastObservation").ValueKind);
        Assert.Single(await api.RowsAsync(journeyId: fresh.JourneyId));
    }

    [RequiresDatabaseTheory]
    [InlineData("2026-10-20T00:05:00+05:30")]
    [InlineData("2026-10-20T23:55:00+05:30")]
    public async Task Midnight_samples_keep_departure_service_date_and_ordered_absolute_stop_times(string text)
    {
        var anchor = DateTimeOffset.Parse(text);
        api.Clock.Set(anchor);
        var rows = await Seed(anchor);
        var today = ColomboTime.ServiceDateOf(anchor);
        foreach (var row in rows)
        {
            var schedule = await api.Client.GetFromJsonAsync<JsonElement>($"/api/journeys?serviceDate={row.ServiceDate:yyyy-MM-dd}");
            var journey = schedule.GetProperty("journeys").EnumerateArray()
                .Single(j => j.GetProperty("journeyId").GetGuid() == row.JourneyId);
            var stops = journey.GetProperty("stops").EnumerateArray().ToArray();
            var departure = stops[0].GetProperty("scheduledDeparture").GetDateTimeOffset();
            var arrival = stops[^1].GetProperty("scheduledArrival").GetDateTimeOffset();
            Assert.Equal(ColomboTime.ServiceDateOf(departure), row.ServiceDate);
            Assert.True(arrival > departure);
            if (row.TrainNumber.EndsWith("OVERNIGHT"))
            {
                Assert.Equal(today.AddDays(-1), row.ServiceDate);
                Assert.Equal(today, ColomboTime.ServiceDateOf(arrival));
            }
            else
            {
                Assert.Equal(anchor.AddMinutes(30), arrival);
            }
        }
    }
}
