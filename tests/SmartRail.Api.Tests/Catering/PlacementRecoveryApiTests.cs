using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Catering.Orders;
using SmartRail.Api.Tests.Operations;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Catering;

// What happens when Grilled does not answer: a timeout is never read as a refusal, the link ID
// never changes, and at most one provider order survives. The proxy loses a request or its
// response on demand; the retry pass is run by hand at times the test chooses.
public class PlacementRecoveryApiTests(CateringApi api) : IClassFixture<CateringApi>
{
    private static int _subjects;

    private async Task<string> NewPassengerAsync()
    {
        var subject = $"recovery-passenger-{Interlocked.Increment(ref _subjects)}";
        await api.AddPassengerAsync(subject);
        return subject;
    }

    private static string Key() => Guid.NewGuid().ToString("N");

    private static Guid IdOf(JsonElement order) => order.GetProperty("id").GetGuid();

    private async Task<Guid> PlaceWith(ProxyMode mode, string passenger, Corridor corridor, string item = "pdn-fish-bun")
    {
        api.Proxy.Mode = mode;
        try
        {
            var response = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", (item, 1));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var order = await response.JsonAsync();
            Assert.Equal("PLACING", order.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, order.GetProperty("pickupCode").ValueKind);
            Assert.Equal($"/api/food/orders/{IdOf(order)}", response.Headers.Location?.ToString());
            return IdOf(order);
        }
        finally
        {
            api.Proxy.Mode = ProxyMode.Forward;
        }
    }

    private async Task PublishEarlyEndAtPolgahawelaAsync(Corridor corridor)
    {
        var subject = $"recovery-controller-{Guid.NewGuid():N}";
        await api.AddAccountAsync(subject, roles: Roles.Controller);
        var preview = await (await api.PreviewAsync(subject, corridor.JourneyId, ChangeBodies.EarlyEnd(corridor.Stop("PLG")))).JsonAsync();
        var version = preview.GetProperty("previewedVersion").GetInt32();
        var confirmed = await api.ConfirmAsync(subject, corridor.JourneyId, ChangeBodies.EarlyEnd(corridor.Stop("PLG")).WithVersion(version));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task A_lost_response_after_the_provider_committed_is_pending_then_confirmed_with_one_provider_order()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var before = await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun");

        var linkId = await PlaceWith(ProxyMode.DropResponse, passenger, corridor);

        // The provider did commit; SmartRail just never heard.
        Assert.NotNull(await api.ProviderOrderAsync(linkId));
        Assert.Equal("PENDING", (await api.LinkAsync(linkId)).PlacementState);
        Assert.Equal(before - 1, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));

        // Not due yet, so a pass does nothing; then it is due and the same link ID is reconciled.
        Assert.Equal(0, await api.RunPlacementPassAsync());
        await api.AdvanceAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, await api.RunPlacementPassAsync());

        var link = await api.LinkAsync(linkId);
        Assert.Equal(("CONFIRMED", "ACCEPTED", null), (link.PlacementState, link.ProviderStatus, link.RequestPayload));
        Assert.Equal((await api.ProviderOrderAsync(linkId))!.Value.GetProperty("id").GetString(), link.ProviderOrderRef);
        Assert.Equal(before - 1, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));

        var read = await (await api.SendAsync(passenger, HttpMethod.Get, $"/api/food/orders/{linkId}")).JsonAsync();
        Assert.Equal("ACCEPTED", read.GetProperty("status").GetString());
        Assert.Equal((await api.ProviderOrderAsync(linkId))!.Value.GetProperty("pickupCode").GetString(), read.GetProperty("pickupCode").GetString());
    }

    [RequiresDatabaseFact]
    public async Task A_lost_request_before_the_provider_saw_it_is_placed_on_retry()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var before = await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun");

        var linkId = await PlaceWith(ProxyMode.DropRequest, passenger, corridor);
        Assert.Null(await api.ProviderOrderAsync(linkId));

        await api.AdvanceAsync(TimeSpan.FromSeconds(5));
        await api.RunPlacementPassAsync();

        Assert.Equal("CONFIRMED", (await api.LinkAsync(linkId)).PlacementState);
        Assert.NotNull(await api.ProviderOrderAsync(linkId));
        Assert.Equal(before - 1, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));
    }

    [RequiresDatabaseFact]
    public async Task Repeated_lost_responses_back_off_and_never_create_a_second_provider_order()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var before = await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun");
        var linkId = await PlaceWith(ProxyMode.DropResponse, passenger, corridor);

        var delays = new List<TimeSpan>();
        api.Proxy.Mode = ProxyMode.DropResponse;
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var due = (await api.LinkAsync(linkId)).NextAttemptAt;
                await api.SetNowAsync(due);
                Assert.Equal(1, await api.RunPlacementPassAsync());
                delays.Add((await api.LinkAsync(linkId)).NextAttemptAt - api.Clock.Now);
            }
        }
        finally
        {
            api.Proxy.Mode = ProxyMode.Forward;
        }

        // Increasing delays: 2 s after the first attempt, then 4, 8 and so on.
        Assert.Equal([TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16)], delays);
        Assert.Equal("PENDING", (await api.LinkAsync(linkId)).PlacementState);
        Assert.Equal(before - 1, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));

        await api.SetNowAsync((await api.LinkAsync(linkId)).NextAttemptAt);
        await api.RunPlacementPassAsync();
        Assert.Equal("CONFIRMED", (await api.LinkAsync(linkId)).PlacementState);
        Assert.Equal(before - 1, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));
    }

    [RequiresDatabaseFact]
    public async Task A_stopped_provider_leaves_the_order_pending_until_it_returns()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        await api.Grilled.StopAsync();
        Guid linkId;
        try
        {
            var response = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            linkId = IdOf(await response.JsonAsync());
        }
        finally
        {
            await api.Grilled.StartAsync();
            await api.Grilled.SetNowAsync(api.Clock.Now);
        }

        Assert.Null(await api.ProviderOrderAsync(linkId));
        await api.AdvanceAsync(TimeSpan.FromSeconds(5));
        await api.RunPlacementPassAsync();

        Assert.Equal("CONFIRMED", (await api.LinkAsync(linkId)).PlacementState);
        Assert.NotNull(await api.ProviderOrderAsync(linkId));
    }

    [RequiresDatabaseFact]
    public async Task A_client_retry_of_a_pending_request_resolves_it_once_the_provider_is_back()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var key = Key();
        api.Proxy.Mode = ProxyMode.DropResponse;
        HttpResponseMessage first;
        try
        {
            first = await api.OrderAsync(passenger, key, corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
        }
        finally
        {
            api.Proxy.Mode = ProxyMode.Forward;
        }

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        await api.AdvanceAsync(TimeSpan.FromSeconds(5));

        var retry = await api.OrderAsync(passenger, key, corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var order = await retry.JsonAsync();
        Assert.Equal(IdOf(await first.JsonAsync()), IdOf(order));
        Assert.Equal("ACCEPTED", order.GetProperty("status").GetString());
    }

    [RequiresDatabaseFact]
    public async Task A_restarted_api_still_has_the_pending_order_and_finishes_it()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var linkId = await PlaceWith(ProxyMode.DropResponse, passenger, corridor);
        await api.AdvanceAsync(TimeSpan.FromSeconds(5));

        // A new host over the same database stands in for the API process starting again.
        using var restarted = api.Factory.WithWebHostBuilder(_ => { });
        await using (var scope = restarted.Services.CreateAsyncScope())
        {
            // Other tests may have left their own pending links due as well.
            Assert.True(await scope.ServiceProvider.GetRequiredService<PlacementProcessor>().RunDueAsync(default) >= 1);
        }

        Assert.Equal("CONFIRMED", (await api.LinkAsync(linkId)).PlacementState);
    }

    [RequiresDatabaseFact]
    public async Task An_early_ending_after_a_lost_response_does_not_hide_the_order_the_provider_accepted()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var linkId = await PlaceWith(ProxyMode.DropResponse, passenger, corridor);

        await PublishEarlyEndAtPolgahawelaAsync(corridor); // Peradeniya is now beyond the final stop
        await api.AdvanceAsync(TimeSpan.FromSeconds(5));
        await api.RunPlacementPassAsync();

        // The retry did not place anything new, but it found the order that was placed. It is not
        // shown as refused. The train-change event (delivered separately) is what cancels it.
        var link = await api.LinkAsync(linkId);
        Assert.Equal(("CONFIRMED", "ACCEPTED"), (link.PlacementState, link.ProviderStatus));
        Assert.NotNull(await api.ProviderOrderAsync(linkId));
    }

    [RequiresDatabaseFact]
    public async Task An_early_ending_after_a_lost_request_stops_the_retry_but_is_not_called_final_until_the_attempt_has_settled()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var before = await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun");
        var linkId = await PlaceWith(ProxyMode.DropRequest, passenger, corridor);

        await PublishEarlyEndAtPolgahawelaAsync(corridor);
        await api.AdvanceAsync(TimeSpan.FromSeconds(5));
        await api.RunPlacementPassAsync();

        // Nothing is placed, and nothing is declared rejected while the lost request might still
        // be arriving.
        var waiting = await api.LinkAsync(linkId);
        Assert.Equal("PENDING", waiting.PlacementState);
        Assert.Null(await api.ProviderOrderAsync(linkId));
        var shown = await (await api.SendAsync(passenger, HttpMethod.Get, $"/api/food/orders/{linkId}")).JsonAsync();
        Assert.Equal("PLACING", shown.GetProperty("status").GetString());

        await api.SetNowAsync(waiting.LastAttemptAt!.Value.AddSeconds(31));
        await api.RunPlacementPassAsync();

        var settled = await api.LinkAsync(linkId);
        Assert.Equal(("REJECTED", "STOP_NOT_SERVED", null), (settled.PlacementState, settled.RejectionCode, settled.RequestPayload));
        Assert.Null(await api.ProviderOrderAsync(linkId));
        Assert.Equal(before, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));
    }

    [RequiresDatabaseFact]
    public async Task While_the_provider_is_unreachable_a_changed_eligibility_never_becomes_a_false_refusal()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var linkId = await PlaceWith(ProxyMode.DropResponse, passenger, corridor);

        await PublishEarlyEndAtPolgahawelaAsync(corridor);
        await api.AdvanceAsync(TimeSpan.FromMinutes(1));
        api.Proxy.Mode = ProxyMode.DropRequest; // the lookup cannot get through either
        try
        {
            await api.RunPlacementPassAsync();
        }
        finally
        {
            api.Proxy.Mode = ProxyMode.Forward;
        }

        Assert.Equal("PENDING", (await api.LinkAsync(linkId)).PlacementState);
    }
}
