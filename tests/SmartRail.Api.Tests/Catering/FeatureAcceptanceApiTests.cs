using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Catering.Outbox;
using SmartRail.Api.Tests.Operations;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Catering;

// The five feature acceptance checks of _specs/railbites.md, each walked end to end against the real
// SmartRail API (PostgreSQL) and the real RailBites process (SQLite), with a hand-moved clock shared
// by both. Each check is one test, named for it.
public class FeatureAcceptanceApiTests(CallbackApi api) : IClassFixture<CallbackApi>
{
    private static int _subjects;

    private async Task<string> NewPassengerAsync()
    {
        var subject = $"acceptance-passenger-{Interlocked.Increment(ref _subjects)}";
        await api.AddPassengerAsync(subject);
        return subject;
    }

    private static string Key() => Guid.NewGuid().ToString("N");

    private static string PointOf(string stop) => stop == "PDN" ? "peradeniya-junction" : "polgahawela-junction";

    private static string KioskKeyOf(string point) =>
        point == "peradeniya-junction" ? RailBitesProcess.PeradeniyaKioskKey : RailBitesProcess.PolgahawelaKioskKey;

    private async Task<Guid> PlaceAsync(string passenger, Corridor corridor, string stop, string item)
    {
        var response = await api.OrderAsync(passenger, Key(), corridor, stop, PointOf(stop), (item, 1));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> ReadAsync(string passenger, Guid order)
    {
        var response = await api.SendAsync(passenger, HttpMethod.Get, $"/api/food/orders/{order}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.JsonAsync();
    }

    private async Task<(string Id, string Code)> ProviderIdsAsync(Guid order)
    {
        var provider = (await api.ProviderOrderAsync(order))!.Value;
        return (provider.GetProperty("id").GetString()!, provider.GetProperty("pickupCode").GetString()!);
    }

    private Task<HttpResponseMessage> KioskAsync(string point, string key, string suffix, string? json = null) =>
        api.RailBites.KioskAsync(point, key, HttpMethod.Post, suffix, json);

    private async Task<Dictionary<string, int>> ReceiptsByLinkAsync(Guid link)
    {
        var rows = new List<int>();
        await api.WithDbAsync(async db => rows = await db.WebhookReceipts.Where(r => r.OrderLinkId == link).Select(r => r.StatusSequence).ToListAsync());
        return rows.GroupBy(r => $"seq{r}").ToDictionary(g => g.Key, g => g.Count());
    }

    private async Task DeliverEverythingAsync()
    {
        await api.AdvanceAsync(TimeSpan.FromSeconds(1));
        await api.RunOutboxPassAsync();
        await api.RunCallbacksAsync();
    }

    [RequiresDatabaseFact]
    public async Task Check_1_browse_place_retry_ready_collect_and_read_Collected_while_other_people_are_denied()
    {
        var passenger = await NewPassengerAsync();
        var stranger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();

        // Browse where to collect and what is for sale.
        var options = await (await api.Client.GetAsync($"/api/journeys/{corridor.JourneyId}/collection-options")).JsonAsync();
        Assert.Contains(options.GetProperty("options").EnumerateArray(), o =>
            o.GetProperty("collectionPoint").GetProperty("id").GetString() == "peradeniya-junction" && o.GetProperty("orderable").GetBoolean());
        var menu = await (await api.Client.GetAsync("/api/food/collection-points/peradeniya-junction/menu")).JsonAsync();
        Assert.Contains(menu.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetString() == "pdn-lamprais");

        // Place, and retry the same request: one order.
        var key = Key();
        var first = await api.OrderAsync(passenger, key, corridor, "PDN", "peradeniya-junction", ("pdn-lamprais", 1));
        var retry = await api.OrderAsync(passenger, key, corridor, "PDN", "peradeniya-junction", ("pdn-lamprais", 1));
        var order = (await first.JsonAsync()).GetProperty("id").GetGuid();
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (first.StatusCode, retry.StatusCode));
        Assert.Equal(order, (await retry.JsonAsync()).GetProperty("id").GetGuid());
        var (providerId, code) = await ProviderIdsAsync(order);
        Assert.Equal(code, (await ReadAsync(passenger, order)).GetProperty("pickupCode").GetString());

        // Another passenger cannot read or cancel it and never sees the code.
        foreach (var attempt in new[]
        {
            await api.SendAsync(stranger, HttpMethod.Get, $"/api/food/orders/{order}"),
            await api.SendAsync(stranger, HttpMethod.Post, $"/api/food/orders/{order}/cancellation"),
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode);
            Assert.DoesNotContain(code, await attempt.Content.ReadAsStringAsync());
        }

        // Another collection point's kiosk cannot act on it, with either of its possible keys.
        var wrongPath = await KioskAsync("polgahawela-junction", RailBitesProcess.PolgahawelaKioskKey, $"/{providerId}/ready");
        var wrongKey = await KioskAsync("peradeniya-junction", RailBitesProcess.PolgahawelaKioskKey, $"/{providerId}/collected", $$"""{"pickupCode":"{{code}}"}""");
        Assert.Equal(HttpStatusCode.NotFound, wrongPath.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongKey.StatusCode);
        Assert.DoesNotContain(code, await wrongPath.Content.ReadAsStringAsync() + await wrongKey.Content.ReadAsStringAsync());
        Assert.Equal("ACCEPTED", (await ReadAsync(passenger, order)).GetProperty("status").GetString());

        // The right kiosk marks it Ready, then collects it with the code; SmartRail shows each step.
        Assert.Equal(HttpStatusCode.OK, (await KioskAsync("peradeniya-junction", RailBitesProcess.PeradeniyaKioskKey, $"/{providerId}/ready")).StatusCode);
        await api.RunCallbacksAsync();
        Assert.Equal("READY", (await ReadAsync(passenger, order)).GetProperty("status").GetString());

        var wrongCode = await KioskAsync("peradeniya-junction", RailBitesProcess.PeradeniyaKioskKey, $"/{providerId}/collected", """{"pickupCode":"nope"}""");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongCode.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await KioskAsync("peradeniya-junction", RailBitesProcess.PeradeniyaKioskKey, $"/{providerId}/collected", $$"""{"pickupCode":"{{code}}"}""")).StatusCode);
        await api.RunCallbacksAsync();

        var collected = await ReadAsync(passenger, order);
        Assert.Equal(("COLLECTED", 3), (collected.GetProperty("status").GetString(), collected.GetProperty("statusSequence").GetInt32()));
        Assert.Equal(new Dictionary<string, int> { ["seq1"] = 1, ["seq2"] = 1, ["seq3"] = 1 }, await ReceiptsByLinkAsync(order));
    }

    [RequiresDatabaseFact]
    public async Task Check_2_a_delay_moves_the_cutoff_unknown_freshness_blocks_ordering_and_no_show_waits_for_departure()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync(); // 10:00, train between Gampaha and Veyangoda
        var atPeradeniya = await PlaceAsync(passenger, corridor, "PDN", "pdn-fish-bun");
        var toCancelEarly = await PlaceAsync(passenger, corridor, "PDN", "pdn-fish-bun");
        var atPolgahawela = await PlaceAsync(passenger, corridor, "PLG", "plg-plain-tea");

        // No-show is refused while the train is still before the stop.
        var (plgId, _) = await ProviderIdsAsync(atPolgahawela);
        var tooSoon = await KioskAsync("polgahawela-junction", RailBitesProcess.PolgahawelaKioskKey, $"/{plgId}/no-show");
        Assert.Equal(HttpStatusCode.Conflict, tooSoon.StatusCode);
        Assert.Equal("TRAIN_NOT_PASSED", (await tooSoon.JsonAsync()).GetProperty("code").GetString());

        // A confirmed 20 minute delay moves the pickup time and, with it, the order-by time.
        await api.PublishAsync(corridor, ChangeBodies.Delay(20));
        await DeliverEverythingAsync();
        var revised = await ReadAsync(passenger, atPeradeniya);
        Assert.Equal(corridor.At("12:10"), revised.GetProperty("pickupTime").GetProperty("estimatedArrival").GetDateTimeOffset());
        Assert.Equal(2, revised.GetProperty("statusSequence").GetInt32());
        var options = await (await api.Client.GetAsync($"/api/journeys/{corridor.JourneyId}/collection-options")).JsonAsync();
        var peradeniya = options.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("stopId").GetGuid() == corridor.Stop("PDN"));
        Assert.Equal(corridor.At("11:40"), peradeniya.GetProperty("orderBy").GetDateTimeOffset());

        // Free cancellation one second before the revised cutoff; refused at it.
        await api.SetNowAsync(corridor.At("11:40").AddSeconds(-1));
        await api.ObserveAsync(corridor, "PLG", 500, api.Clock.Now.AddSeconds(-5));
        Assert.Equal(HttpStatusCode.OK, (await api.SendAsync(passenger, HttpMethod.Post, $"/api/food/orders/{toCancelEarly}/cancellation")).StatusCode);
        await api.SetNowAsync(corridor.At("11:40"));
        await api.ObserveAsync(corridor, "PLG", 500, api.Clock.Now.AddSeconds(-5));
        var refused = await api.SendAsync(passenger, HttpMethod.Post, $"/api/food/orders/{atPeradeniya}/cancellation");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("CUTOFF_PASSED", (await refused.JsonAsync()).GetProperty("code").GetString());
        Assert.Equal("ACCEPTED", (await ReadAsync(passenger, atPeradeniya)).GetProperty("status").GetString());

        // The train has now been seen leaving Polgahawela, so No-show is accepted and reaches the passenger.
        var noShow = await KioskAsync("polgahawela-junction", RailBitesProcess.PolgahawelaKioskKey, $"/{plgId}/no-show");
        Assert.Equal(HttpStatusCode.OK, noShow.StatusCode);
        await api.RunCallbacksAsync();
        Assert.Equal("NO_SHOW", (await ReadAsync(passenger, atPolgahawela)).GetProperty("status").GetString());

        // With no sighting for five minutes the train is unknown and ordering is blocked.
        await api.AdvanceAsync(TimeSpan.FromMinutes(5));
        var blocked = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
        Assert.Equal("TRAIN_UNKNOWN", (await blocked.JsonAsync()).GetProperty("code").GetString());
    }

    [RequiresDatabaseFact]
    public async Task Check_3_an_early_ending_with_RailBites_stopped_cancels_once_after_it_returns_and_a_withdrawal_reopens_ordering()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var atFinal = await PlaceAsync(passenger, corridor, "PLG", "plg-plain-tea");
        var beyond = await PlaceAsync(passenger, corridor, "PDN", "pdn-fish-bun");
        var buns = await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun");

        await api.RailBites.StopAsync();
        Guid ending;
        try
        {
            ending = await api.PublishAsync(corridor, ChangeBodies.EarlyEnd(corridor.Stop("PLG")));
            api.Clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(await api.RunOutboxPassAsync() >= 1);

            // Train information is unaffected by the provider being down.
            Assert.Equal(HttpStatusCode.OK, (await api.Client.GetAsync($"/api/journeys/{corridor.JourneyId}/running")).StatusCode);
            var changes = await (await api.Client.GetAsync($"/api/journeys/{corridor.JourneyId}/changes")).JsonAsync();
            Assert.Equal("ENDS_EARLY", changes.GetProperty("availability").GetString());
        }
        finally
        {
            await api.RailBites.StartAsync();
            await api.RailBites.SetNowAsync(api.Clock.Now);
        }

        await api.AdvanceAsync(TimeSpan.FromSeconds(5));
        await api.RunOutboxPassAsync();
        await api.RunCallbacksAsync();

        var cancelled = await ReadAsync(passenger, beyond);
        Assert.Equal(("CANCELLED", "TRAIN_ENDED_EARLY", 2), (cancelled.GetProperty("status").GetString(), cancelled.GetProperty("reasonCode").GetString(), cancelled.GetProperty("statusSequence").GetInt32()));
        Assert.Equal(buns + 1, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));
        Assert.Equal("ACCEPTED", (await ReadAsync(passenger, atFinal)).GetProperty("status").GetString());
        Assert.Equal(new Dictionary<string, int> { ["seq1"] = 1, ["seq2"] = 1 }, await ReceiptsByLinkAsync(beyond));

        // Withdraw the ending and redeliver the old event: ordering reopens, the cancelled order stays cancelled.
        var oldEvent = (await api.OutboxOfAsync(corridor)).First().Payload;
        await api.PublishAsync(corridor, ChangeBodies.Withdraw(ending));
        await DeliverEverythingAsync();
        var replay = await api.RailBites.Http.PostAsync("/v1/journey-events", new StringContent(oldEvent, Encoding.UTF8, "application/json"));
        Assert.Equal("DUPLICATE", (await replay.JsonAsync()).GetProperty("result").GetString());

        Assert.Equal("CANCELLED", (await ReadAsync(passenger, beyond)).GetProperty("status").GetString());
        var again = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal("CANCELLED", (await ReadAsync(passenger, beyond)).GetProperty("status").GetString());
    }

    [RequiresDatabaseFact]
    public async Task Check_4_lost_placement_responses_on_both_sides_of_the_commit_then_an_early_ending_leave_no_ineligible_order_and_no_false_refusal()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var buns = await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun");

        async Task<Guid> PlaceLostAsync(ProxyMode mode)
        {
            api.Proxy.Mode = mode;
            try
            {
                var response = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
                return (await response.JsonAsync()).GetProperty("id").GetGuid();
            }
            finally
            {
                api.Proxy.Mode = ProxyMode.Forward;
            }
        }

        var committedThenLost = await PlaceLostAsync(ProxyMode.DropResponse); // RailBites has this order
        var lostBeforeCommit = await PlaceLostAsync(ProxyMode.DropRequest);   // RailBites never saw this one
        Assert.NotNull(await api.ProviderOrderAsync(committedThenLost));
        Assert.Null(await api.ProviderOrderAsync(lostBeforeCommit));

        await api.PublishAsync(corridor, ChangeBodies.EarlyEnd(corridor.Stop("PLG")));
        await DeliverEverythingAsync();
        await api.AdvanceAsync(TimeSpan.FromSeconds(5));
        await api.RunPlacementPassAsync();
        await api.RunCallbacksAsync();

        // The order RailBites accepted was cancelled with a reason; the passenger is told, not left waiting.
        var accepted = await ReadAsync(passenger, committedThenLost);
        Assert.Equal(("CANCELLED", "TRAIN_ENDED_EARLY"), (accepted.GetProperty("status").GetString(), accepted.GetProperty("reasonCode").GetString()));

        // The other is not called refused while its lost request might still be arriving.
        Assert.Equal("PLACING", (await ReadAsync(passenger, lostBeforeCommit)).GetProperty("status").GetString());

        // If that request does arrive late, describing the journey before the change, RailBites refuses it.
        var late = $$"""
            {"collectionPointId":"peradeniya-junction",
             "journey":{"journeyId":"{{corridor.JourneyId}}","trainNumber":"SIM-3001","serviceDate":"{{corridor.Date:yyyy-MM-dd}}","version":0,"orderClosed":false,"blockedPickupStopIds":[]},
             "pickup":{"stopId":"{{corridor.Stop("PDN")}}","stationId":"04111fa9-fc9c-3657-e13b-d606dbe37c55","scheduledArrival":"{{corridor.At("11:50"):yyyy-MM-dd'T'HH:mm:ss.fff'Z'}}","estimatedArrival":"{{corridor.At("11:50"):yyyy-MM-dd'T'HH:mm:ss.fff'Z'}}","etaEvidenceAt":"{{api.Clock.Now.AddSeconds(-10):yyyy-MM-dd'T'HH:mm:ss.fff'Z'}}"},
             "items":[{"menuItemId":"pdn-fish-bun","quantity":1}]}
            """;
        var arrived = await api.RailBites.Http.PutAsync($"/v1/order-links/{lostBeforeCommit}", new StringContent(late, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, arrived.StatusCode);

        // Once the lost attempt has settled, and only then, the passenger sees a final refusal.
        await api.AdvanceAsync(TimeSpan.FromSeconds(35));
        await api.RunPlacementPassAsync();
        var refused = await ReadAsync(passenger, lostBeforeCommit);
        Assert.Equal(("REJECTED", "STOP_NOT_SERVED"), (refused.GetProperty("status").GetString(), refused.GetProperty("reasonCode").GetString()));

        // Neither order survives at RailBites as live, and stock is whole again.
        Assert.Null(await api.ProviderOrderAsync(lostBeforeCommit));
        Assert.Equal("CANCELLED", (await api.ProviderOrderAsync(committedThenLost))!.Value.GetProperty("status").GetString());
        Assert.Equal(buns, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));
    }

    [RequiresDatabaseFact]
    public async Task Check_5_SmartRail_down_during_kiosk_updates_then_both_services_restarted_converge_without_regression_or_double_stock()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var order = await PlaceAsync(passenger, corridor, "PDN", "pdn-lamprais");
        await api.RunCallbacksAsync(); // the first callback gets through
        var (providerId, code) = await ProviderIdsAsync(order);
        var stockAfterOrder = await api.ProviderStockAsync("peradeniya-junction", "pdn-lamprais");

        // SmartRail "stops" while the kiosk marks the order Ready and Collected.
        api.Bridge.Mode = ProxyMode.ServiceUnavailable;
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await KioskAsync("peradeniya-junction", RailBitesProcess.PeradeniyaKioskKey, $"/{providerId}/ready")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await KioskAsync("peradeniya-junction", RailBitesProcess.PeradeniyaKioskKey, $"/{providerId}/collected", $$"""{"pickupCode":"{{code}}"}""")).StatusCode);
            for (var i = 0; i < 3; i++)
            {
                await api.AdvanceAsync(TimeSpan.FromSeconds(20));
                await api.RunCallbacksAsync();
            }
        }
        finally
        {
            api.Bridge.Mode = ProxyMode.Forward;
        }

        Assert.Equal("ACCEPTED", (await ReadAsync(passenger, order)).GetProperty("status").GetString());
        var waiting = (await api.RailBites.Http.GetFromJsonAsync<JsonElement>("/internal/test/callbacks")).GetProperty("callbacks")
            .EnumerateArray().Where(c => c.GetProperty("deliveredAt").ValueKind == JsonValueKind.Null).ToList();
        Assert.Equal(2, waiting.Count);

        // Restart RailBites: the undelivered callbacks were in its database, and still are.
        await api.RailBites.RestartAsync();
        await api.RailBites.SetNowAsync(api.Clock.Now);
        waiting = (await api.RailBites.Http.GetFromJsonAsync<JsonElement>("/internal/test/callbacks")).GetProperty("callbacks")
            .EnumerateArray().Where(c => c.GetProperty("deliveredAt").ValueKind == JsonValueKind.Null).ToList();
        Assert.Equal(2, waiting.Count);

        // SmartRail "restarts" (a new host over the same database) and the retries converge on Collected.
        using var restarted = api.Factory.WithWebHostBuilder(_ => { });
        await api.AdvanceAsync(TimeSpan.FromMinutes(2));
        await api.RunCallbacksAsync();

        var client = restarted.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", api.Token(passenger));
        var final = await (await client.GetAsync($"/api/food/orders/{order}")).JsonAsync();
        Assert.Equal(("COLLECTED", 3), (final.GetProperty("status").GetString(), final.GetProperty("statusSequence").GetInt32()));
        Assert.Equal(new Dictionary<string, int> { ["seq1"] = 1, ["seq2"] = 1, ["seq3"] = 1 }, await ReceiptsByLinkAsync(order));

        // Repeating delivery changes nothing: no regression, no extra receipts, no stock effect.
        await api.AdvanceAsync(TimeSpan.FromMinutes(2));
        await api.RunCallbacksAsync();
        Assert.Equal("COLLECTED", (await ReadAsync(passenger, order)).GetProperty("status").GetString());
        Assert.Equal(stockAfterOrder, await api.ProviderStockAsync("peradeniya-junction", "pdn-lamprais"));
        Assert.All((await api.RailBites.Http.GetFromJsonAsync<JsonElement>("/internal/test/callbacks")).GetProperty("callbacks").EnumerateArray()
            .Where(c => c.GetProperty("eventId").GetString()!.StartsWith(providerId)), c => Assert.NotEqual(JsonValueKind.Null, c.GetProperty("deliveredAt").ValueKind));

        // Both services restarted: the order, its receipts and its delivered state are still there.
        await api.RailBites.RestartAsync();
        await api.RailBites.SetNowAsync(api.Clock.Now);
        Assert.Equal("COLLECTED", (await api.ProviderOrderAsync(order))!.Value.GetProperty("status").GetString());
        var afterBoth = await (await restarted.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/food/orders/{order}")
        {
            Headers = { Authorization = new("Bearer", api.Token(passenger)) },
        })).JsonAsync();
        Assert.Equal("COLLECTED", afterBoth.GetProperty("status").GetString());
    }
}
