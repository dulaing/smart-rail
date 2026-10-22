using SmartRail.Api.Tests.Rail;
using System.Net;
using System.Text.Json;
using SmartRail.Api.Tests.Operations;

namespace SmartRail.Api.Tests.Catering;

// Guests browse where food can be collected on a train and what is on the menu. Real Grilled,
// disposable PostgreSQL, a hand-moved clock.
public class BrowseApiTests(CateringApi api) : IClassFixture<CateringApi>
{
    private async Task<JsonElement> OptionsAsync(Guid journeyId)
    {
        var response = await api.Client.GetAsync($"/api/journeys/{journeyId}/collection-options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return await response.JsonAsync();
    }

    private static JsonElement Option(JsonElement options, string collectionPoint) =>
        options.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("collectionPoint").GetProperty("id").GetString() == collectionPoint);

    [RequiresDatabaseFact]
    public async Task A_running_train_offers_both_collection_points_with_the_providers_cutoff()
    {
        var corridor = await api.RunningCorridorAsync();

        var options = await OptionsAsync(corridor.JourneyId);

        Assert.Equal("SIM-3001", options.GetProperty("trainNumber").GetString());
        Assert.Equal(2, options.GetProperty("options").GetArrayLength());
        var peradeniya = Option(options, "peradeniya-junction");
        Assert.True(peradeniya.GetProperty("orderable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, peradeniya.GetProperty("reasonCode").ValueKind);
        Assert.Equal(corridor.Stop("PDN"), peradeniya.GetProperty("stopId").GetGuid());
        Assert.Equal(30, peradeniya.GetProperty("collectionPoint").GetProperty("cutoffMinutes").GetInt32());
        Assert.Equal(corridor.At("11:50"), peradeniya.GetProperty("estimatedArrival").GetDateTimeOffset());
        // Order-by is the ETA less the provider's cutoff.
        Assert.Equal(corridor.At("11:20"), peradeniya.GetProperty("orderBy").GetDateTimeOffset());
        Assert.Equal(corridor.At("10:30"), Option(options, "polgahawela-junction").GetProperty("orderBy").GetDateTimeOffset());
    }

    [RequiresDatabaseFact]
    public async Task A_stop_the_train_has_left_is_not_offered()
    {
        var corridor = await api.RunningCorridorAsync();
        await api.AdvanceAsync(TimeSpan.FromMinutes(70)); // 11:10
        await api.ObserveAsync(corridor, "PLG", 300, api.Clock.Now.AddSeconds(-10));

        var options = await OptionsAsync(corridor.JourneyId);

        var polgahawela = Option(options, "polgahawela-junction");
        Assert.False(polgahawela.GetProperty("orderable").GetBoolean());
        Assert.Equal("STOP_PASSED", polgahawela.GetProperty("reasonCode").GetString());
        Assert.True(Option(options, "peradeniya-junction").GetProperty("orderable").GetBoolean());
    }

    [RequiresDatabaseFact]
    public async Task An_unknown_train_offers_nothing_and_says_why()
    {
        var corridor = await api.RunningCorridorAsync();
        await api.AdvanceAsync(TimeSpan.FromSeconds(240)); // the 10-second-old sighting is now 250 seconds old

        var options = await OptionsAsync(corridor.JourneyId);

        foreach (var option in options.GetProperty("options").EnumerateArray())
        {
            Assert.False(option.GetProperty("orderable").GetBoolean());
            Assert.Equal("TRAIN_UNKNOWN", option.GetProperty("reasonCode").GetString());
            Assert.Equal(JsonValueKind.Null, option.GetProperty("estimatedArrival").ValueKind);
        }
    }

    [RequiresDatabaseFact]
    public async Task A_journey_on_another_day_is_not_offered()
    {
        var corridor = await api.RunningCorridorAsync();
        await api.SetNowAsync(corridor.At("10:00").AddDays(-1));

        var options = await OptionsAsync(corridor.JourneyId);

        Assert.All(options.GetProperty("options").EnumerateArray(), option =>
            Assert.Equal("JOURNEY_NOT_TODAY", option.GetProperty("reasonCode").GetString()));
    }

    [RequiresDatabaseFact]
    public async Task An_unknown_or_malformed_journey_is_404_or_not_routed()
    {
        var unknown = await api.Client.GetAsync($"/api/journeys/{Guid.NewGuid()}/collection-options");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("JOURNEY_NOT_FOUND", (await unknown.JsonAsync()).GetProperty("code").GetString());
    }

    [RequiresDatabaseFact]
    public async Task The_menu_is_public_priced_in_minor_LKR_and_cached_for_a_minute()
    {
        var first = await api.Client.GetAsync("/api/food/collection-points/peradeniya-junction/menu");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("public, max-age=60", first.Headers.CacheControl?.ToString());
        var menu = await first.JsonAsync();
        var lamprais = menu.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == "pdn-lamprais");
        Assert.Equal(85000, lamprais.GetProperty("priceMinorLkr").GetInt32());

        // Within the minute SmartRail answers from its cache, so a stopped provider is not noticed.
        await api.Grilled.StopAsync();
        try
        {
            var cached = await api.Client.GetAsync("/api/food/collection-points/peradeniya-junction/menu");
            Assert.Equal(HttpStatusCode.OK, cached.StatusCode);

            // A menu it has never fetched cannot be invented.
            var missing = await api.Client.GetAsync("/api/food/collection-points/polgahawela-junction/menu");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, missing.StatusCode);
            Assert.Equal("PROVIDER_UNAVAILABLE", (await missing.JsonAsync()).GetProperty("code").GetString());
        }
        finally
        {
            await api.Grilled.StartAsync();
            await api.Grilled.SetNowAsync(api.Clock.Now);
        }
    }

    [RequiresDatabaseFact]
    public async Task An_unknown_collection_point_is_404()
    {
        var response = await api.Client.GetAsync("/api/food/collection-points/nowhere/menu");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task With_the_provider_down_collection_options_are_unavailable_but_train_information_is_not()
    {
        var corridor = await api.RunningCorridorAsync();
        await api.Grilled.StopAsync();
        try
        {
            var options = await api.Client.GetAsync($"/api/journeys/{corridor.JourneyId}/collection-options");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, options.StatusCode);

            var running = await api.Client.GetAsync($"/api/journeys/{corridor.JourneyId}/running");
            Assert.Equal(HttpStatusCode.OK, running.StatusCode);
        }
        finally
        {
            await api.Grilled.StartAsync();
            await api.Grilled.SetNowAsync(api.Clock.Now);
        }
    }
}
