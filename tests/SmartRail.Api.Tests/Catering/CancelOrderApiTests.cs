using System.Net;
using System.Text.Json;
using SmartRail.Api.Tests.Operations;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Catering;

// A passenger cancels their own order. SmartRail sends a fresh snapshot; RailBites applies its own
// cutoff. Real RailBites, disposable PostgreSQL, hand-moved clock.
public class CancelOrderApiTests(CateringApi api) : IClassFixture<CateringApi>
{
    private static int _subjects;

    private async Task<string> NewPassengerAsync()
    {
        var subject = $"cancel-passenger-{Interlocked.Increment(ref _subjects)}";
        await api.AddPassengerAsync(subject);
        return subject;
    }

    private static string Key() => Guid.NewGuid().ToString("N");

    private async Task<Guid> OrderAsync(string passenger, Corridor corridor, string item = "pdn-fish-bun")
    {
        var response = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", (item, 1));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> CancelAsync(string? passenger, Guid orderId) =>
        api.SendAsync(passenger, HttpMethod.Post, $"/api/food/orders/{orderId}/cancellation");

    private static string CodeOf(JsonElement problem) => problem.GetProperty("code").GetString()!;

    [RequiresDatabaseFact]
    public async Task Before_the_cutoff_a_passenger_cancels_and_the_provider_returns_the_stock()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var before = await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun");
        var orderId = await OrderAsync(passenger, corridor);
        Assert.Equal(before - 1, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));

        var response = await CancelAsync(passenger, orderId);
        var order = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("CANCELLED", order.GetProperty("status").GetString());
        Assert.Equal("PASSENGER_CANCELLED", order.GetProperty("reasonCode").GetString());
        Assert.Equal(before, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));
        Assert.Equal("CANCELLED", (await api.ProviderOrderAsync(orderId))!.Value.GetProperty("status").GetString());
        var link = await api.LinkAsync(orderId);
        Assert.Equal(("CANCELLED", 2), (link.ProviderStatus, link.StatusSequence));

        // Cancelling again is harmless and returns nothing twice.
        var again = await CancelAsync(passenger, orderId);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(before, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));

        // They can order again straight away.
        Assert.Equal(HttpStatusCode.Created, (await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1))).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task At_the_providers_cutoff_cancellation_is_refused_and_one_second_earlier_it_is_allowed()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var early = await OrderAsync(passenger, corridor);
        var late = await OrderAsync(passenger, corridor);
        var cutoff = corridor.At("11:20");

        await api.SetNowAsync(cutoff.AddSeconds(-1));
        await api.ObserveAsync(corridor, "PLG", 500, api.Clock.Now.AddSeconds(-5));
        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(passenger, early)).StatusCode);

        await api.SetNowAsync(cutoff);
        await api.ObserveAsync(corridor, "PLG", 500, api.Clock.Now.AddSeconds(-5));
        var refused = await CancelAsync(passenger, late);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("CUTOFF_PASSED", CodeOf(await refused.JsonAsync()));
        Assert.Equal("ACCEPTED", (await api.LinkAsync(late)).ProviderStatus);
    }

    [RequiresDatabaseFact]
    public async Task A_delay_moves_the_revised_cutoff_so_a_later_cancellation_is_still_allowed()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var orderId = await OrderAsync(passenger, corridor);

        // At 11:20 the original cutoff has passed, but the train is now seen 20 minutes late, so
        // Peradeniya is expected at 12:10 and the cutoff is 11:40.
        await api.SetNowAsync(corridor.At("11:20"));
        await api.ObserveAsync(corridor, "PLG", 500, api.Clock.Now.AddSeconds(-5), delaySeconds: 20 * 60);

        var response = await CancelAsync(passenger, orderId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task An_unknown_train_cannot_confirm_the_cutoff_so_cancellation_is_refused()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var orderId = await OrderAsync(passenger, corridor);

        await api.AdvanceAsync(TimeSpan.FromMinutes(5));
        var response = await CancelAsync(passenger, orderId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("TRAIN_UNKNOWN", CodeOf(await response.JsonAsync()));
        Assert.Equal("ACCEPTED", (await api.LinkAsync(orderId)).ProviderStatus);
    }

    [RequiresDatabaseFact]
    public async Task Only_the_owner_can_cancel()
    {
        var owner = await NewPassengerAsync();
        var other = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var orderId = await OrderAsync(owner, corridor);

        Assert.Equal(HttpStatusCode.NotFound, (await CancelAsync(other, orderId)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CancelAsync(null, orderId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CancelAsync(owner, Guid.NewGuid())).StatusCode);
        Assert.Equal("ACCEPTED", (await api.LinkAsync(orderId)).ProviderStatus);
    }

    [RequiresDatabaseFact]
    public async Task With_the_provider_down_cancellation_changes_nothing_and_says_so()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var orderId = await OrderAsync(passenger, corridor);

        await api.RailBites.StopAsync();
        try
        {
            var response = await CancelAsync(passenger, orderId);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("PROVIDER_UNAVAILABLE", CodeOf(await response.JsonAsync()));
        }
        finally
        {
            await api.RailBites.StartAsync();
            await api.RailBites.SetNowAsync(api.Clock.Now);
        }

        Assert.Equal("ACCEPTED", (await api.LinkAsync(orderId)).ProviderStatus);
        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(passenger, orderId)).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task A_collected_order_cannot_be_cancelled()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var orderId = await OrderAsync(passenger, corridor);
        var provider = (await api.ProviderOrderAsync(orderId))!.Value;
        var providerId = provider.GetProperty("id").GetString();
        var code = provider.GetProperty("pickupCode").GetString();

        Assert.Equal(HttpStatusCode.OK, (await api.RailBites.KioskAsync("peradeniya-junction", RailBitesProcess.PeradeniyaKioskKey, HttpMethod.Post, $"/{providerId}/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.RailBites.KioskAsync("peradeniya-junction", RailBitesProcess.PeradeniyaKioskKey, HttpMethod.Post, $"/{providerId}/collected", $$"""{"pickupCode":"{{code}}"}""")).StatusCode);

        var response = await CancelAsync(passenger, orderId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("INVALID_STATE", CodeOf(await response.JsonAsync()));
    }

    [RequiresDatabaseFact]
    public async Task A_pending_or_refused_order_cannot_be_cancelled()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();

        api.Proxy.Mode = ProxyMode.DropResponse;
        Guid pending;
        try
        {
            pending = (await (await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1))).JsonAsync()).GetProperty("id").GetGuid();
        }
        finally
        {
            api.Proxy.Mode = ProxyMode.Forward;
        }

        var pendingResponse = await CancelAsync(passenger, pending);
        Assert.Equal(HttpStatusCode.Conflict, pendingResponse.StatusCode);
        Assert.Equal("PLACEMENT_PENDING", CodeOf(await pendingResponse.JsonAsync()));

        var refused = await (await api.OrderAsync(passenger, Key(), corridor, "VEY", "peradeniya-junction", ("pdn-fish-bun", 1))).JsonAsync();
        Assert.Equal("PICKUP_MISMATCH", CodeOf(refused));
        var refusedResponse = await CancelAsync(passenger, refused.GetProperty("orderId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, refusedResponse.StatusCode);
        Assert.Equal("INVALID_STATE", CodeOf(await refusedResponse.JsonAsync()));
    }
}
