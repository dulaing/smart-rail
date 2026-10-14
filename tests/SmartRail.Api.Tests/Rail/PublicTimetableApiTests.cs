using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Tests.Rail;

// Each class gets its own database, so a test that changes data cannot disturb another class.

public class StationsApiTests(TimetableApi api) : IClassFixture<TimetableApi>
{
    private async Task<JsonElement> GetStationsAsync()
    {
        var response = await api.Client.GetAsync("/api/stations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [RequiresDatabaseFact]
    public async Task Anonymous_request_lists_stations_and_a_second_migrate_and_seed_changes_nothing()
    {
        // No Authorization header is sent: the client is anonymous.
        var first = await GetStationsAsync();
        await api.MigrateAndSeedAsync();
        var second = await GetStationsAsync();

        Assert.Equal(5, first.GetArrayLength());
        Assert.Equal(first.GetRawText(), second.GetRawText());

        var codes = first.EnumerateArray().Select(s => s.GetProperty("code").GetString()).ToList();
        Assert.Equal(["FOT", "GPH", "MDA", "RGM", "VEY"], codes);
        Assert.Equal(codes.Count, first.EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).Distinct().Count());
        Assert.Equal("Gampaha", first.EnumerateArray().Single(s => s.GetProperty("code").GetString() == "GPH")
            .GetProperty("nameEn").GetString());
    }

    [RequiresDatabaseFact]
    public async Task Reseeding_does_not_duplicate_patterns_or_journeys()
    {
        int patterns = 0, journeys = 0;
        await api.WithDbAsync(async db =>
        {
            patterns = await db.ServicePatterns.CountAsync();
            journeys = await db.Journeys.CountAsync();
        });

        await api.MigrateAndSeedAsync();

        await api.WithDbAsync(async db =>
        {
            Assert.Equal(patterns, await db.ServicePatterns.CountAsync());
            Assert.Equal(journeys, await db.Journeys.CountAsync());
        });
        Assert.Equal(5, patterns);
    }
}

public class JourneysApiTests(TimetableApi api) : IClassFixture<TimetableApi>
{
    private async Task<JsonElement> GetJourneysAsync(string date)
    {
        var response = await api.Client.GetAsync($"/api/journeys?serviceDate={date}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<string?> Trains(JsonElement body) =>
        body.GetProperty("journeys").EnumerateArray().Select(j => j.GetProperty("trainNumber").GetString()).ToList();

    [RequiresDatabaseFact]
    public async Task Service_dates_show_their_own_journeys()
    {
        var wednesday = await GetJourneysAsync("2026-10-14");
        var saturday = await GetJourneysAsync("2026-10-17");

        Assert.Equal(["SIM-1001", "SIM-1002", "SIM-1003", "SIM-2001", "SIM-9001"], Trains(wednesday));
        // The weekday-only trains do not run on Saturday.
        Assert.Equal(["SIM-1003", "SIM-2001", "SIM-9001"], Trains(saturday));
    }

    [RequiresDatabaseFact]
    public async Task Journey_lists_ordered_stops_with_instants_platforms_and_the_simulation_label()
    {
        var body = await GetJourneysAsync("2026-10-14");
        var journey = body.GetProperty("journeys").EnumerateArray()
            .Single(j => j.GetProperty("trainNumber").GetString() == "SIM-9001");

        Assert.Equal("FICTIONAL_DEMO", journey.GetProperty("sourceKind").GetString());
        Assert.Equal("SmartRail simulation", journey.GetProperty("sourceLabel").GetString());
        Assert.Equal(JsonValueKind.Null, journey.GetProperty("sourceUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, journey.GetProperty("sourceCheckedAt").ValueKind);
        Assert.Equal("Scheduled times only — not live", body.GetProperty("scheduleNote").GetString());

        var stops = journey.GetProperty("stops").EnumerateArray().ToList();
        Assert.Equal([1, 2, 3], stops.Select(s => s.GetProperty("sequence").GetInt32()));
        Assert.Equal(["FOT", "RGM", "GPH"], stops.Select(s => s.GetProperty("stationCode").GetString()));
        Assert.Equal("6", stops[0].GetProperty("plannedPlatform").GetString());
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 23, 45, 0, TimeSpan.FromMinutes(330)),
            stops[0].GetProperty("scheduledDeparture").GetDateTimeOffset());
        // After midnight: the next calendar day, still service date 2026-10-14.
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 0, 45, 0, TimeSpan.FromMinutes(330)),
            stops[2].GetProperty("scheduledArrival").GetDateTimeOffset());
    }

    [RequiresDatabaseFact]
    public async Task A_date_with_no_journeys_returns_an_empty_list()
    {
        var body = await GetJourneysAsync("2027-03-01");

        Assert.Empty(Trains(body));
    }

    [RequiresDatabaseFact]
    public async Task Missing_or_malformed_date_is_a_clear_400()
    {
        foreach (var query in new[] { "", "?serviceDate=", "?serviceDate=14-10-2026", "?serviceDate=2026-02-30", "?serviceDate=tomorrow" })
        {
            var response = await api.Client.GetAsync("/api/journeys" + query);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("serviceDate", problem.GetProperty("detail").GetString());
        }
    }

    [RequiresDatabaseFact]
    public async Task Database_rejects_a_published_pattern_without_a_checked_source()
    {
        await api.WithDbAsync(async db =>
        {
            db.ServicePatterns.Add(new ServicePattern
            {
                Id = Guid.NewGuid(),
                TrainNumber = "PUB-NO-SOURCE",
                SourceKind = SourceKind.Published,
                EffectiveFrom = TimetableApi.SeedStart,
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        });
    }

    [RequiresDatabaseFact]
    public async Task Published_pattern_with_a_checked_source_is_labelled_as_a_published_timetable()
    {
        var checkedAt = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
        var gampaha = Guid.Empty;
        var fort = Guid.Empty;
        await api.WithDbAsync(async db =>
        {
            gampaha = (await db.Stations.SingleAsync(s => s.Code == "GPH")).Id;
            fort = (await db.Stations.SingleAsync(s => s.Code == "FOT")).Id;
            var pattern = new ServicePattern
            {
                Id = Guid.NewGuid(),
                TrainNumber = "PUB-1",
                SourceKind = SourceKind.Published,
                SourceUrl = "https://example.org/timetable.pdf",
                SourceCheckedAt = checkedAt,
                EffectiveFrom = new DateOnly(2026, 12, 1),
                Stops =
                [
                    new PatternStop { Id = Guid.NewGuid(), StationId = gampaha, Sequence = 1, DepartureLocal = new TimeOnly(7, 35) },
                    new PatternStop { Id = Guid.NewGuid(), StationId = fort, Sequence = 2, ArrivalLocal = new TimeOnly(8, 30) },
                ],
            };
            db.ServicePatterns.Add(pattern);
            db.Journeys.Add(new ScheduledJourney
            {
                Id = Guid.NewGuid(), PatternId = pattern.Id, ServiceDate = new DateOnly(2026, 12, 2), Pattern = pattern,
            });
            await db.SaveChangesAsync();
        });

        var body = await GetJourneysAsync("2026-12-02");

        var journey = Assert.Single(body.GetProperty("journeys").EnumerateArray());
        Assert.Equal("PUBLISHED", journey.GetProperty("sourceKind").GetString());
        Assert.Equal("Published timetable", journey.GetProperty("sourceLabel").GetString());
        Assert.Equal("https://example.org/timetable.pdf", journey.GetProperty("sourceUrl").GetString());
        Assert.Equal(checkedAt, journey.GetProperty("sourceCheckedAt").GetDateTimeOffset());
    }
}

public class NextTrainsApiTests(TimetableApi api) : IClassFixture<TimetableApi>
{
    private async Task<Guid> StationIdAsync(string code)
    {
        var stations = await api.Client.GetFromJsonAsync<JsonElement>("/api/stations");
        return stations.EnumerateArray().Single(s => s.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> NextAsync(string from, string to, string? at = null)
    {
        var url = $"/api/next-trains?originId={await StationIdAsync(from)}&destinationId={await StationIdAsync(to)}";
        if (at is not null)
        {
            url += "&at=" + Uri.EscapeDataString(at);
        }

        return await api.Client.GetAsync(url);
    }

    private static async Task<List<JsonElement>> TrainsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Scheduled times only — not live", body.GetProperty("scheduleNote").GetString());
        return body.GetProperty("trains").EnumerateArray().ToList();
    }

    [RequiresDatabaseFact]
    public async Task Gampaha_to_Fort_returns_three_eligible_trains_in_departure_order()
    {
        var trains = await TrainsAsync(await NextAsync("GPH", "FOT", "2026-10-14T06:30:00+05:30"));

        Assert.Equal(["SIM-1003", "SIM-1001", "SIM-1002"], trains.Select(t => t.GetProperty("trainNumber").GetString()));
        var first = trains[0];
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 7, 20, 0, TimeSpan.FromMinutes(330)),
            first.GetProperty("originScheduledDeparture").GetDateTimeOffset());
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 8, 15, 0, TimeSpan.FromMinutes(330)),
            first.GetProperty("destinationScheduledArrival").GetDateTimeOffset());
        Assert.Equal("1", first.GetProperty("plannedPlatform").GetString());
        Assert.Equal("SmartRail simulation", first.GetProperty("sourceLabel").GetString());
        Assert.NotEqual(Guid.Empty, first.GetProperty("journeyId").GetGuid());
        Assert.NotEqual(Guid.Empty, first.GetProperty("originStopId").GetGuid());
        Assert.NotEqual(Guid.Empty, first.GetProperty("destinationStopId").GetGuid());
    }

    [RequiresDatabaseFact]
    public async Task The_same_instant_in_another_offset_gives_the_same_trains()
    {
        var colombo = await TrainsAsync(await NextAsync("GPH", "FOT", "2026-10-14T07:30:00+05:30"));
        var utc = await TrainsAsync(await NextAsync("GPH", "FOT", "2026-10-14T02:00:00Z"));

        Assert.Equal(colombo.Select(t => t.GetRawText()), utc.Select(t => t.GetRawText()));
        Assert.Equal("SIM-1001", colombo[0].GetProperty("trainNumber").GetString());
    }

    [RequiresDatabaseFact]
    public async Task Early_morning_request_matches_the_overnight_train_from_the_previous_service_date()
    {
        var trains = await TrainsAsync(await NextAsync("RGM", "GPH", "2026-10-15T00:20:00+05:30"));

        var overnight = trains[0];
        Assert.Equal("SIM-9001", overnight.GetProperty("trainNumber").GetString());
        Assert.Equal("2026-10-14", overnight.GetProperty("serviceDate").GetString());
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 0, 26, 0, TimeSpan.FromMinutes(330)),
            overnight.GetProperty("originScheduledDeparture").GetDateTimeOffset());
    }

    [RequiresDatabaseFact]
    public async Task A_valid_route_with_no_trains_returns_an_empty_list()
    {
        // Veyangoda is the end of the line in the seed, so nothing runs from it.
        var trains = await TrainsAsync(await NextAsync("VEY", "FOT", "2026-10-14T06:00:00+05:30"));

        Assert.Empty(trains);
    }

    [RequiresDatabaseFact]
    public async Task Malformed_requests_are_400_and_unknown_stations_are_404()
    {
        var gampaha = await StationIdAsync("GPH");
        var fort = await StationIdAsync("FOT");

        string[] bad =
        [
            "/api/next-trains",
            $"/api/next-trains?originId={gampaha}",
            $"/api/next-trains?originId=nope&destinationId={fort}",
            $"/api/next-trains?originId={gampaha}&destinationId={gampaha}",
            $"/api/next-trains?originId={gampaha}&destinationId={fort}&at=2026-10-14T07:00:00",
            $"/api/next-trains?originId={gampaha}&destinationId={fort}&at=tomorrow",
        ];
        foreach (var url in bad)
        {
            var response = await api.Client.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{url} returned {(int)response.StatusCode}");
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
        }

        var unknown = await api.Client.GetAsync($"/api/next-trains?originId={Guid.NewGuid()}&destinationId={fort}");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}

public class ETagApiTests(TimetableApi api) : IClassFixture<TimetableApi>
{
    private async Task<HttpResponseMessage> GetAsync(string url, string? ifNoneMatch = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        return await api.Client.SendAsync(request);
    }

    [RequiresDatabaseFact]
    public async Task Unchanged_reads_get_304_and_a_changed_timetable_gets_a_new_etag()
    {
        var stations = await api.Client.GetFromJsonAsync<JsonElement>("/api/stations");
        var ids = stations.EnumerateArray().ToDictionary(
            s => s.GetProperty("code").GetString()!, s => s.GetProperty("id").GetGuid());
        string[] urls =
        [
            "/api/stations",
            "/api/journeys?serviceDate=2026-10-14",
            $"/api/next-trains?originId={ids["GPH"]}&destinationId={ids["FOT"]}&at=2026-10-14T06:30:00%2B05:30",
        ];

        var tags = new Dictionary<string, string>();
        foreach (var url in urls)
        {
            var first = await GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var etag = first.Headers.ETag!.Tag;
            tags[url] = etag;

            var again = await GetAsync(url, etag);
            Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
            Assert.Equal(etag, again.Headers.ETag!.Tag);

            var stale = await GetAsync(url, "\"something-else\"");
            Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        }

        // Change a platform that the journeys and next-trains answers both show.
        await api.WithDbAsync(async db =>
        {
            var pattern = await db.ServicePatterns.SingleAsync(p => p.TrainNumber == "SIM-1003");
            var changed = await db.PatternStops
                .Where(s => s.PatternId == pattern.Id && s.Sequence == 1)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.PlannedPlatform, "9"));
            Assert.Equal(1, changed);
        });

        Assert.Equal(tags[urls[0]], (await GetAsync(urls[0])).Headers.ETag!.Tag);
        foreach (var url in urls.Skip(1))
        {
            var response = await GetAsync(url, tags[url]);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEqual(tags[url], response.Headers.ETag!.Tag);
        }
    }
}
