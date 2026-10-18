using SmartRail.Api.Tests.Rail;
using System.Net;
using System.Text.Json;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Catering.Orders;
using SmartRail.Api.Tests.Access;
using SmartRail.Api.Tests.Operations;

namespace SmartRail.Api.Tests.Catering;

// A passenger places, reads, lists and cancels food orders through SmartRail while a real
// RailBites process holds the menu, stock and orders. Disposable PostgreSQL, a hand-moved clock.
public class OrderApiTests(CateringApi api) : IClassFixture<CateringApi>
{
    private static int _subjects;

    private async Task<string> NewPassengerAsync()
    {
        var subject = $"order-passenger-{Interlocked.Increment(ref _subjects)}";
        await api.AddPassengerAsync(subject);
        return subject;
    }

    private static string Key() => Guid.NewGuid().ToString("N");

    private static Guid IdOf(JsonElement order) => order.GetProperty("id").GetGuid();

    [RequiresDatabaseFact]
    public async Task A_passenger_orders_and_reads_back_their_order_with_a_pickup_code()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var before = await api.ProviderStockAsync("peradeniya-junction", "pdn-lamprais");

        var created = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-lamprais", 2));
        var order = await created.JsonAsync();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"/api/food/orders/{IdOf(order)}", created.Headers.Location?.ToString());
        Assert.Equal("ACCEPTED", order.GetProperty("status").GetString());
        Assert.Matches(@"^\d{6}$", order.GetProperty("pickupCode").GetString());
        Assert.Equal(170000, order.GetProperty("totalMinorLkr").GetInt32());
        Assert.Equal(corridor.At("11:50"), order.GetProperty("pickupTime").GetProperty("estimatedArrival").GetDateTimeOffset());
        Assert.Equal(before - 2, await api.ProviderStockAsync("peradeniya-junction", "pdn-lamprais"));

        // The provider holds one order, under SmartRail's link ID.
        var provider = await api.ProviderOrderAsync(IdOf(order));
        Assert.Equal(order.GetProperty("pickupCode").GetString(), provider!.Value.GetProperty("pickupCode").GetString());

        var read = await (await api.SendAsync(passenger, HttpMethod.Get, $"/api/food/orders/{IdOf(order)}")).JsonAsync();
        Assert.Equal(order.GetRawText(), read.GetRawText());
        var list = await (await api.SendAsync(passenger, HttpMethod.Get, "/api/food/orders")).JsonAsync();
        Assert.Equal(IdOf(order), IdOf(Assert.Single(list.GetProperty("orders").EnumerateArray())));

        // The request key is not stored where the provider can see it.
        var link = await api.LinkAsync(IdOf(order));
        Assert.Null(link.RequestPayload);
        Assert.NotEqual(link.ClientRequestKey, link.Id.ToString());
    }

    [RequiresDatabaseFact]
    public async Task Repeating_a_request_returns_the_same_order_without_a_second_provider_order()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var key = Key();
        var before = await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun");

        var first = await api.OrderAsync(passenger, key, corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
        var again = await api.OrderAsync(passenger, key, corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(IdOf(await first.JsonAsync()), IdOf(await again.JsonAsync()));
        Assert.Equal(before - 1, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));
    }

    [RequiresDatabaseFact]
    public async Task Concurrent_identical_submissions_make_one_link_and_one_provider_order()
    {
        var passenger = await NewPassengerAsync();
        var account = await api.AddAccountAsync($"{passenger}-b", roles: Roles.Passenger);
        var corridor = await api.RunningCorridorAsync();
        var key = Key();
        var before = await api.ProviderStockAsync("peradeniya-junction", "pdn-string-hoppers");

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            api.OrderAsync($"{passenger}-b", key, corridor, "PDN", "peradeniya-junction", ("pdn-string-hoppers", 1))));

        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Accepted }));
        Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.Created) <= 1);
        var ids = (await Task.WhenAll(responses.Select(async r => IdOf(await r.JsonAsync())))).Distinct().ToList();
        Assert.Single(ids);
        Assert.Equal(1, await api.LinkCountAsync(account));
        Assert.Equal(before - 1, await api.ProviderStockAsync("peradeniya-junction", "pdn-string-hoppers"));
        Assert.Equal("CONFIRMED", (await api.LinkAsync(ids[0])).PlacementState);
    }

    [RequiresDatabaseFact]
    public async Task A_key_reused_for_a_different_order_is_409_and_changes_nothing()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var key = Key();
        var order = await (await api.OrderAsync(passenger, key, corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1))).JsonAsync();
        var before = await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun");

        var otherItems = await api.OrderAsync(passenger, key, corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 2));
        var otherStop = await api.OrderAsync(passenger, key, corridor, "PLG", "polgahawela-junction", ("plg-plain-tea", 1));

        Assert.Equal(HttpStatusCode.Conflict, otherItems.StatusCode);
        Assert.Equal("CLIENT_REQUEST_KEY_IN_USE", (await otherItems.JsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, otherStop.StatusCode);
        Assert.Equal(before, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));
        Assert.Equal("ACCEPTED", (await api.LinkAsync(IdOf(order))).ProviderStatus);
    }

    [RequiresDatabaseFact]
    public async Task The_same_key_from_two_accounts_makes_two_orders_and_neither_can_read_the_others()
    {
        var alice = await NewPassengerAsync();
        var bob = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var key = Key();

        var a = await (await api.OrderAsync(alice, key, corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1))).JsonAsync();
        var b = await (await api.OrderAsync(bob, key, corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1))).JsonAsync();

        Assert.NotEqual(IdOf(a), IdOf(b));
        Assert.NotEqual(a.GetProperty("pickupCode").GetString(), b.GetProperty("pickupCode").GetString());

        var peek = await api.SendAsync(bob, HttpMethod.Get, $"/api/food/orders/{IdOf(a)}");
        Assert.Equal(HttpStatusCode.NotFound, peek.StatusCode);
        Assert.DoesNotContain(a.GetProperty("pickupCode").GetString()!, await peek.Content.ReadAsStringAsync());

        var cancel = await api.SendAsync(bob, HttpMethod.Post, $"/api/food/orders/{IdOf(a)}/cancellation");
        Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
        Assert.Equal("ACCEPTED", (await api.LinkAsync(IdOf(a))).ProviderStatus);

        var bobsList = await (await api.SendAsync(bob, HttpMethod.Get, "/api/food/orders")).JsonAsync();
        Assert.Equal(IdOf(b), IdOf(Assert.Single(bobsList.GetProperty("orders").EnumerateArray())));
    }

    [RequiresDatabaseFact]
    public async Task Ordering_needs_a_signed_in_enabled_passenger()
    {
        await api.AddAccountAsync("food-driver", roles: Roles.Driver);
        await api.AddAccountAsync("food-disabled", status: AccountStatus.Disabled, roles: Roles.Passenger);
        var corridor = await api.RunningCorridorAsync();

        var anonymous = await api.OrderAsync(null, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
        var driver = await api.OrderAsync("food-driver", Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
        var disabled = await api.OrderAsync("food-disabled", Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, driver.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, disabled.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.SendAsync(null, HttpMethod.Get, "/api/food/orders")).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task A_malformed_request_is_400()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        object[] bad =
        [
            new { },
            new { clientRequestKey = Key(), collectionPointId = "peradeniya-junction", journeyId = corridor.JourneyId, pickupStopId = corridor.Stop("PDN"), items = Array.Empty<object>() },
            new { clientRequestKey = Key(), collectionPointId = "peradeniya-junction", journeyId = corridor.JourneyId, pickupStopId = corridor.Stop("PDN"), items = new[] { new { menuItemId = "pdn-fish-bun", quantity = 0 } } },
            new { clientRequestKey = Key(), collectionPointId = "peradeniya-junction", journeyId = corridor.JourneyId, pickupStopId = corridor.Stop("PDN"), items = new[] { new { menuItemId = "pdn-fish-bun", quantity = 1 }, new { menuItemId = "pdn-fish-bun", quantity = 1 } } },
        ];
        foreach (var body in bad)
        {
            var response = await api.SendAsync(passenger, HttpMethod.Post, OrderEndpoints.Path, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await api.SendAsync(passenger, HttpMethod.Get, $"/api/food/orders/{Guid.NewGuid()}")).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task A_stop_that_is_not_on_the_journey_or_that_the_train_has_left_is_refused_before_anything_is_stored()
    {
        var passenger = await NewPassengerAsync();
        var account = await api.AddAccountAsync($"{passenger}-c", roles: Roles.Passenger);
        var corridor = await api.RunningCorridorAsync();
        // A stop of a different train pattern, so it is not one of this journey's stop occurrences.
        var (_, otherTrainStops) = await api.JourneyWithStopsAsync();

        var wrongJourney = await api.SendAsync($"{passenger}-c", HttpMethod.Post, OrderEndpoints.Path, new
        {
            clientRequestKey = Key(), collectionPointId = "peradeniya-junction", journeyId = corridor.JourneyId,
            pickupStopId = otherTrainStops[1].StopId, items = new[] { new { menuItemId = "pdn-fish-bun", quantity = 1 } },
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongJourney.StatusCode);
        Assert.Equal("STOP_NOT_FOUND", (await wrongJourney.JsonAsync()).GetProperty("code").GetString());

        await api.AdvanceAsync(TimeSpan.FromMinutes(70)); // 11:10, past Polgahawela
        await api.ObserveAsync(corridor, "PLG", 300, api.Clock.Now.AddSeconds(-10));
        var passed = await api.OrderAsync($"{passenger}-c", Key(), corridor, "PLG", "polgahawela-junction", ("plg-plain-tea", 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, passed.StatusCode);
        Assert.Equal("STOP_PASSED", (await passed.JsonAsync()).GetProperty("code").GetString());

        Assert.Equal(0, await api.LinkCountAsync(account));
    }

    [RequiresDatabaseFact]
    public async Task An_unknown_train_blocks_a_new_order_but_leaves_an_existing_one_alone()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var order = await (await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1))).JsonAsync();

        await api.AdvanceAsync(TimeSpan.FromMinutes(5)); // no sighting for five minutes
        var blocked = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
        Assert.Equal("TRAIN_UNKNOWN", (await blocked.JsonAsync()).GetProperty("code").GetString());
        var read = await (await api.SendAsync(passenger, HttpMethod.Get, $"/api/food/orders/{IdOf(order)}")).JsonAsync();
        Assert.Equal("ACCEPTED", read.GetProperty("status").GetString());
    }

    [RequiresDatabaseFact]
    public async Task Not_enough_stock_is_a_final_refusal_with_a_reason_that_a_repeat_returns_again()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();

        // Take lamprais until fewer than 20 are left, so a request for 20 cannot be met.
        while (await api.ProviderStockAsync("peradeniya-junction", "pdn-lamprais") >= 20)
        {
            var drain = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-lamprais", 20));
            Assert.Equal(HttpStatusCode.Created, drain.StatusCode);
        }

        var lampraisLeft = await api.ProviderStockAsync("peradeniya-junction", "pdn-lamprais");
        var bunsLeft = await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun");
        var key = Key();

        var refused = await api.OrderAsync(passenger, key, corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1), ("pdn-lamprais", 20));
        var body = await refused.JsonAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("STOCK_INSUFFICIENT", body.GetProperty("code").GetString());
        var linkId = body.GetProperty("orderId").GetGuid();
        var link = await api.LinkAsync(linkId);
        Assert.Equal(("REJECTED", "STOCK_INSUFFICIENT", null), (link.PlacementState, link.RejectionCode, link.RequestPayload));
        Assert.Null(await api.ProviderOrderAsync(linkId));
        Assert.Equal(lampraisLeft, await api.ProviderStockAsync("peradeniya-junction", "pdn-lamprais"));
        Assert.Equal(bunsLeft, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));

        // The same key gives the same answer; it does not ask the provider again.
        var repeat = await api.OrderAsync(passenger, key, corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1), ("pdn-lamprais", 20));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, repeat.StatusCode);
        Assert.Equal(linkId, (await repeat.JsonAsync()).GetProperty("orderId").GetGuid());

        var order = await (await api.SendAsync(passenger, HttpMethod.Get, $"/api/food/orders/{linkId}")).JsonAsync();
        Assert.Equal("REJECTED", order.GetProperty("status").GetString());
        Assert.Equal("STOCK_INSUFFICIENT", order.GetProperty("reasonCode").GetString());
        Assert.Equal(JsonValueKind.Null, order.GetProperty("pickupCode").ValueKind);
    }

    [RequiresDatabaseFact]
    public async Task A_stop_with_no_matching_collection_point_is_refused_by_the_provider()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();

        // Veyangoda is ahead and served, but RailBites has no collection point there.
        var response = await api.OrderAsync(passenger, Key(), corridor, "VEY", "peradeniya-junction", ("pdn-fish-bun", 1));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("PICKUP_MISMATCH", (await response.JsonAsync()).GetProperty("code").GetString());
    }

    [RequiresDatabaseFact]
    public async Task The_providers_cutoff_decides_and_equality_is_closed()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var peradeniyaClosesAt = corridor.At("11:20");

        await api.SetNowAsync(peradeniyaClosesAt.AddSeconds(-1));
        await api.ObserveAsync(corridor, "PLG", 500, api.Clock.Now.AddSeconds(-5));
        var open = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
        Assert.Equal(HttpStatusCode.Created, open.StatusCode);

        await api.SetNowAsync(peradeniyaClosesAt);
        await api.ObserveAsync(corridor, "PLG", 500, api.Clock.Now.AddSeconds(-5));
        var closed = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, closed.StatusCode);
        Assert.Equal("CUTOFF_PASSED", (await closed.JsonAsync()).GetProperty("code").GetString());
    }

    [RequiresDatabaseFact]
    public async Task The_menu_reads_stay_public_while_every_order_route_is_protected()
    {
        var corridor = await api.RunningCorridorAsync();
        foreach (var path in new[] { "/api/food/orders", $"/api/food/orders/{Guid.NewGuid()}" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await api.SendAsync(null, HttpMethod.Get, path)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.SendAsync(null, HttpMethod.Post, $"/api/food/orders/{Guid.NewGuid()}/cancellation")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Client.GetAsync($"/api/journeys/{corridor.JourneyId}/collection-options")).StatusCode);
    }
}
