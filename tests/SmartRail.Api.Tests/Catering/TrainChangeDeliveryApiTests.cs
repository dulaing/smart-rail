using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;
using SmartRail.Api.Modules.Catering.Outbox;
using SmartRail.Api.Tests.Operations;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Catering;

// A confirmed train change reaches RailBites after an outage and is applied once. Real RailBites
// (Node + SQLite), disposable PostgreSQL, a hand-moved clock, and a proxy that can lose a response
// or answer 503. The dispatcher pass is run by hand at times the test chooses.
public class TrainChangeDeliveryApiTests(CateringApi api) : IClassFixture<CateringApi>
{
    private static int _subjects;

    private async Task<string> NewPassengerAsync()
    {
        var subject = $"change-passenger-{Interlocked.Increment(ref _subjects)}";
        await api.AddPassengerAsync(subject);
        return subject;
    }

    private static string Key() => Guid.NewGuid().ToString("N");

    private async Task<Guid> OrderAsync(string passenger, Corridor corridor, string stop, string item)
    {
        var point = stop == "PDN" ? "peradeniya-junction" : "polgahawela-junction";
        var response = await api.OrderAsync(passenger, Key(), corridor, stop, point, (item, 1));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> ProviderAsync(Guid linkId) => (await api.ProviderOrderAsync(linkId))!.Value;

    private static async Task<JsonElement> JsonAsyncOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // Delivers everything due, then makes sure nothing is left waiting.
    private async Task DeliverAllAsync()
    {
        await api.AdvanceAsync(TimeSpan.FromSeconds(1));
        await api.RunOutboxPassAsync();
    }

    [RequiresDatabaseFact]
    public async Task A_confirmed_delay_is_queued_with_post_change_pickup_times_and_moves_open_orders()
    {
        var corridor = await api.RunningCorridorAsync();
        var order = await OrderAsync(await NewPassengerAsync(), corridor, "PDN", "pdn-fish-bun");

        await api.PublishAsync(corridor, ChangeBodies.Delay(20));

        // The queued message already holds the times after the change: 20 minutes later than 11:50.
        var message = Assert.Single(await api.OutboxOfAsync(corridor));
        Assert.Equal((PayloadVersions.WithPickupTimes, DeliveryStates.Pending), (message.PayloadVersion, message.DeliveryState));
        var stops = JsonDocument.Parse(message.Payload).RootElement.GetProperty("stops").EnumerateArray().ToList();
        var peradeniya = stops.Single(s => s.GetProperty("stopId").GetGuid() == corridor.Stop("PDN"));
        Assert.Equal(corridor.At("12:10"), peradeniya.GetProperty("estimatedArrival").GetDateTimeOffset());
        Assert.Equal(corridor.At("10:00").AddSeconds(-10), peradeniya.GetProperty("etaEvidenceAt").GetDateTimeOffset());

        await DeliverAllAsync();

        var provider = await ProviderAsync(order);
        Assert.Equal(corridor.At("12:10"), provider.GetProperty("pickupTime").GetProperty("estimatedArrival").GetDateTimeOffset());
        Assert.Equal(2, provider.GetProperty("statusSequence").GetInt32());
        Assert.Equal("ACCEPTED", provider.GetProperty("status").GetString());
        var delivered = Assert.Single(await api.OutboxOfAsync(corridor));
        Assert.Equal(DeliveryStates.Delivered, delivered.DeliveryState);
        Assert.NotNull(delivered.DeliveredAt);
    }

    [RequiresDatabaseFact]
    public async Task A_delay_confirmed_while_the_train_is_unknown_leaves_the_pickup_time_unknown_not_scheduled()
    {
        var corridor = await api.RunningCorridorAsync();
        var order = await OrderAsync(await NewPassengerAsync(), corridor, "PDN", "pdn-fish-bun");
        await api.AdvanceAsync(TimeSpan.FromMinutes(5)); // no sighting for five minutes

        await api.PublishAsync(corridor, ChangeBodies.Delay(10));
        await DeliverAllAsync();

        var pickup = (await ProviderAsync(order)).GetProperty("pickupTime");
        Assert.Equal(JsonValueKind.Null, pickup.GetProperty("estimatedArrival").ValueKind);
        Assert.Equal(JsonValueKind.Null, pickup.GetProperty("etaEvidenceAt").ValueKind);
        Assert.Equal(corridor.At("11:50"), pickup.GetProperty("scheduledArrival").GetDateTimeOffset());
    }

    [RequiresDatabaseFact]
    public async Task A_cancellation_cancels_open_orders_with_a_reason_and_returns_stock_once()
    {
        var corridor = await api.RunningCorridorAsync();
        var passenger = await NewPassengerAsync();
        var first = await OrderAsync(passenger, corridor, "PDN", "pdn-fish-bun");
        var second = await OrderAsync(passenger, corridor, "PLG", "plg-plain-tea");
        var buns = await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun");

        await api.PublishAsync(corridor, ChangeBodies.Cancel());
        await DeliverAllAsync();

        foreach (var link in new[] { first, second })
        {
            var order = await ProviderAsync(link);
            Assert.Equal(("CANCELLED", "TRAIN_CANCELLED"), (order.GetProperty("status").GetString(), order.GetProperty("cancellationReason").GetString()));
        }

        Assert.Equal(buns + 1, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));
        // A cancelled journey takes no new order.
        var refused = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("STOP_NOT_SERVED", (await refused.JsonAsync()).GetProperty("code").GetString());
    }

    [RequiresDatabaseFact]
    public async Task An_early_ending_keeps_the_final_stop_and_cancels_only_beyond_it()
    {
        var corridor = await api.RunningCorridorAsync();
        var passenger = await NewPassengerAsync();
        var atFinal = await OrderAsync(passenger, corridor, "PLG", "plg-plain-tea");
        var beyond = await OrderAsync(passenger, corridor, "PDN", "pdn-fish-bun");

        await api.PublishAsync(corridor, ChangeBodies.EarlyEnd(corridor.Stop("PLG")));
        await DeliverAllAsync();

        Assert.Equal("ACCEPTED", (await ProviderAsync(atFinal)).GetProperty("status").GetString());
        var cancelled = await ProviderAsync(beyond);
        Assert.Equal(("CANCELLED", "TRAIN_ENDED_EARLY"), (cancelled.GetProperty("status").GetString(), cancelled.GetProperty("cancellationReason").GetString()));
    }

    [RequiresDatabaseFact]
    public async Task A_withdrawal_reopens_ordering_but_a_cancelled_order_stays_cancelled()
    {
        var corridor = await api.RunningCorridorAsync();
        var passenger = await NewPassengerAsync();
        var beyond = await OrderAsync(passenger, corridor, "PDN", "pdn-fish-bun");
        var ending = await api.PublishAsync(corridor, ChangeBodies.EarlyEnd(corridor.Stop("PLG")));
        await DeliverAllAsync();
        Assert.Equal("CANCELLED", (await ProviderAsync(beyond)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1))).StatusCode);

        await api.PublishAsync(corridor, ChangeBodies.Withdraw(ending));
        await DeliverAllAsync();

        Assert.Equal("CANCELLED", (await ProviderAsync(beyond)).GetProperty("status").GetString());
        var reopened = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
        Assert.Equal(HttpStatusCode.Created, reopened.StatusCode);

        // Delivering the old early ending again changes nothing.
        var oldMessage = (await api.OutboxOfAsync(corridor)).First();
        var replay = await api.RailBites.Http.PostAsync("/v1/journey-events", new StringContent(oldMessage.Payload, Encoding.UTF8, "application/json"));
        Assert.Equal("DUPLICATE", (await JsonAsyncOf(replay)).GetProperty("result").GetString());
        Assert.Equal(HttpStatusCode.Created, (await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1))).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task Collected_and_no_show_orders_keep_their_history_through_a_cancellation()
    {
        var corridor = await api.RunningCorridorAsync();
        var passenger = await NewPassengerAsync();
        var collectedLink = await OrderAsync(passenger, corridor, "PDN", "pdn-fish-bun");
        var collected = await ProviderAsync(collectedLink);
        var id = collected.GetProperty("id").GetString();
        await api.RailBites.KioskAsync("peradeniya-junction", RailBitesProcess.PeradeniyaKioskKey, HttpMethod.Post, $"/{id}/ready");
        var done = await api.RailBites.KioskAsync("peradeniya-junction", RailBitesProcess.PeradeniyaKioskKey, HttpMethod.Post, $"/{id}/collected",
            $$"""{"pickupCode":"{{collected.GetProperty("pickupCode").GetString()}}"}""");
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);

        await api.PublishAsync(corridor, ChangeBodies.Cancel());
        await DeliverAllAsync();

        Assert.Equal("COLLECTED", (await ProviderAsync(collectedLink)).GetProperty("status").GetString());
    }

    [RequiresDatabaseFact]
    public async Task While_RailBites_answers_503_the_change_waits_with_growing_delays_and_train_reads_keep_working()
    {
        var corridor = await api.RunningCorridorAsync();
        var order = await OrderAsync(await NewPassengerAsync(), corridor, "PDN", "pdn-fish-bun");
        api.Proxy.Mode = ProxyMode.ServiceUnavailable;

        try
        {
            await api.PublishAsync(corridor, ChangeBodies.EarlyEnd(corridor.Stop("PLG")));
            var delays = new List<TimeSpan>();
            for (var i = 0; i < 3; i++)
            {
                await api.AdvanceAsync(TimeSpan.FromSeconds(1) + (i == 0 ? TimeSpan.Zero : delays[^1]));
                Assert.True(await api.RunOutboxPassAsync() >= 1);
                var row = Assert.Single(await api.OutboxOfAsync(corridor));
                Assert.Equal((DeliveryStates.Pending, i + 1), (row.DeliveryState, row.AttemptCount));
                delays.Add(row.NextAttemptAt - api.Clock.Now);
            }

            Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)], delays);

            // Public train information does not depend on the food provider.
            Assert.Equal(HttpStatusCode.OK, (await api.Client.GetAsync($"/api/journeys/{corridor.JourneyId}/running")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await api.Client.GetAsync($"/api/journeys/{corridor.JourneyId}/changes")).StatusCode);
            Assert.Equal("ACCEPTED", (await ProviderAsync(order)).GetProperty("status").GetString());
        }
        finally
        {
            api.Proxy.Mode = ProxyMode.Forward;
        }

        await api.AdvanceAsync(TimeSpan.FromSeconds(9));
        Assert.True(await api.RunOutboxPassAsync() >= 1);
        Assert.Equal(DeliveryStates.Delivered, Assert.Single(await api.OutboxOfAsync(corridor)).DeliveryState);
        Assert.Equal("CANCELLED", (await ProviderAsync(order)).GetProperty("status").GetString());
    }

    [RequiresDatabaseFact]
    public async Task A_stopped_RailBites_gets_the_change_after_it_restarts_with_its_data_intact()
    {
        var corridor = await api.RunningCorridorAsync();
        var order = await OrderAsync(await NewPassengerAsync(), corridor, "PDN", "pdn-fish-bun");

        await api.RailBites.StopAsync();
        try
        {
            await api.PublishAsync(corridor, ChangeBodies.EarlyEnd(corridor.Stop("PLG")));
            api.Clock.Advance(TimeSpan.FromSeconds(1)); // RailBites is down, so only SmartRail's clock moves
            Assert.True(await api.RunOutboxPassAsync() >= 1);
            Assert.Equal(DeliveryStates.Pending, Assert.Single(await api.OutboxOfAsync(corridor)).DeliveryState);

            // The train reads still work with the provider down.
            Assert.Equal(HttpStatusCode.OK, (await api.Client.GetAsync($"/api/journeys/{corridor.JourneyId}/running")).StatusCode);
        }
        finally
        {
            await api.RailBites.StartAsync();
            await api.RailBites.SetNowAsync(api.Clock.Now);
        }

        await api.AdvanceAsync(TimeSpan.FromSeconds(5));
        await api.RunOutboxPassAsync();

        Assert.Equal(DeliveryStates.Delivered, Assert.Single(await api.OutboxOfAsync(corridor)).DeliveryState);
        var cancelled = await ProviderAsync(order);
        Assert.Equal(("CANCELLED", "TRAIN_ENDED_EARLY"), (cancelled.GetProperty("status").GetString(), cancelled.GetProperty("cancellationReason").GetString()));
    }

    [RequiresDatabaseFact]
    public async Task A_crash_after_RailBites_committed_is_redelivered_and_applied_only_once()
    {
        var corridor = await api.RunningCorridorAsync();
        var order = await OrderAsync(await NewPassengerAsync(), corridor, "PDN", "pdn-fish-bun");
        var buns = await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun");
        await api.PublishAsync(corridor, ChangeBodies.EarlyEnd(corridor.Stop("PLG")));

        // RailBites commits the event, but the acknowledgement is lost on the way back.
        api.Proxy.Mode = ProxyMode.DropResponse;
        try
        {
            await DeliverAllAsync();
        }
        finally
        {
            api.Proxy.Mode = ProxyMode.Forward;
        }

        Assert.Equal(DeliveryStates.Pending, Assert.Single(await api.OutboxOfAsync(corridor)).DeliveryState);
        Assert.Equal("CANCELLED", (await ProviderAsync(order)).GetProperty("status").GetString());
        Assert.Equal(buns + 1, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));

        await api.AdvanceAsync(TimeSpan.FromSeconds(5));
        await api.RunOutboxPassAsync();

        Assert.Equal(DeliveryStates.Delivered, Assert.Single(await api.OutboxOfAsync(corridor)).DeliveryState);
        Assert.Equal(buns + 1, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));
        Assert.Equal(2, (await ProviderAsync(order)).GetProperty("statusSequence").GetInt32());
    }

    [RequiresDatabaseFact]
    public async Task An_older_change_that_arrives_after_a_newer_one_cannot_undo_it()
    {
        var corridor = await api.RunningCorridorAsync();
        var order = await OrderAsync(await NewPassengerAsync(), corridor, "PDN", "pdn-fish-bun");
        await api.PublishAsync(corridor, ChangeBodies.Delay(20)); // version 1
        await api.PublishAsync(corridor, ChangeBodies.EarlyEnd(corridor.Stop("PLG"))); // version 2
        var rows = await api.OutboxOfAsync(corridor);
        Assert.Equal(2, rows.Count);

        // The newer message gets there first, then the dispatcher sends both in order.
        var first = await api.RailBites.Http.PostAsync("/v1/journey-events", new StringContent(rows[1].Payload, Encoding.UTF8, "application/json"));
        Assert.Equal("APPLIED", (await JsonAsyncOf(first)).GetProperty("result").GetString());
        await DeliverAllAsync();

        Assert.All(await api.OutboxOfAsync(corridor), r => Assert.Equal(DeliveryStates.Delivered, r.DeliveryState));
        var provider = await ProviderAsync(order);
        Assert.Equal("CANCELLED", provider.GetProperty("status").GetString());
        var older = await api.RailBites.Http.PostAsync("/v1/journey-events", new StringContent(rows[0].Payload, Encoding.UTF8, "application/json"));
        Assert.Equal("DUPLICATE", (await JsonAsyncOf(older)).GetProperty("result").GetString());
    }

    [RequiresDatabaseFact]
    public async Task An_accepted_order_whose_response_was_lost_is_cancelled_by_the_early_ending_and_the_retry_reports_it()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var buns = await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun");

        api.Proxy.Mode = ProxyMode.DropResponse;
        Guid link;
        try
        {
            var response = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            link = (await response.JsonAsync()).GetProperty("id").GetGuid();
        }
        finally
        {
            api.Proxy.Mode = ProxyMode.Forward;
        }

        await api.PublishAsync(corridor, ChangeBodies.EarlyEnd(corridor.Stop("PLG")));
        await DeliverAllAsync();
        await api.AdvanceAsync(TimeSpan.FromSeconds(5));
        await api.RunPlacementPassAsync();

        // The provider cancelled the order that SmartRail never heard about, and the lookup says so.
        var shown = await (await api.SendAsync(passenger, HttpMethod.Get, $"/api/food/orders/{link}")).JsonAsync();
        Assert.Equal(("CANCELLED", "TRAIN_ENDED_EARLY"), (shown.GetProperty("status").GetString(), shown.GetProperty("reasonCode").GetString()));
        Assert.Equal(buns, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));
    }

    [RequiresDatabaseFact]
    public async Task A_lost_request_that_arrives_after_the_early_ending_is_refused_and_no_order_survives()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var buns = await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun");

        api.Proxy.Mode = ProxyMode.DropRequest;
        Guid link;
        try
        {
            var response = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            link = (await response.JsonAsync()).GetProperty("id").GetGuid();
        }
        finally
        {
            api.Proxy.Mode = ProxyMode.Forward;
        }

        await api.PublishAsync(corridor, ChangeBodies.EarlyEnd(corridor.Stop("PLG")));
        await DeliverAllAsync();

        // The original request turns up late, still describing the journey before the change.
        var late = $$"""
            {"collectionPointId":"peradeniya-junction",
             "journey":{"journeyId":"{{corridor.JourneyId}}","trainNumber":"SIM-3001","serviceDate":"{{corridor.Date:yyyy-MM-dd}}","version":0,"orderClosed":false,"blockedPickupStopIds":[]},
             "pickup":{"stopId":"{{corridor.Stop("PDN")}}","stationId":"04111fa9-fc9c-3657-e13b-d606dbe37c55","scheduledArrival":"{{corridor.At("11:50"):yyyy-MM-dd'T'HH:mm:ss.fff'Z'}}","estimatedArrival":"{{corridor.At("11:50"):yyyy-MM-dd'T'HH:mm:ss.fff'Z'}}","etaEvidenceAt":"{{api.Clock.Now.AddSeconds(-10):yyyy-MM-dd'T'HH:mm:ss.fff'Z'}}"},
             "items":[{"menuItemId":"pdn-fish-bun","quantity":1}]}
            """;
        var arrived = await api.RailBites.Http.PutAsync($"/v1/order-links/{link}", new StringContent(late, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, arrived.StatusCode);
        Assert.Equal("STOP_NOT_SERVED", (await JsonAsyncOf(arrived)).GetProperty("code").GetString());
        Assert.Null(await api.ProviderOrderAsync(link));
        Assert.Equal(buns, await api.ProviderStockAsync("peradeniya-junction", "pdn-fish-bun"));

        // SmartRail does not call it refused until the lost attempt has settled, then it is final.
        await api.AdvanceAsync(TimeSpan.FromSeconds(5));
        await api.RunPlacementPassAsync();
        Assert.Equal("PENDING", (await api.LinkAsync(link)).PlacementState);
        await api.AdvanceAsync(TimeSpan.FromSeconds(31));
        await api.RunPlacementPassAsync();
        var settled = await api.LinkAsync(link);
        Assert.Equal(("REJECTED", "STOP_NOT_SERVED"), (settled.PlacementState, settled.RejectionCode));
    }

    [RequiresDatabaseFact]
    public async Task A_message_queued_in_the_old_format_is_rewritten_never_marked_delivered_and_then_sent()
    {
        var corridor = await api.RunningCorridorAsync();
        var order = await OrderAsync(await NewPassengerAsync(), corridor, "PDN", "pdn-fish-bun");
        var changeId = await api.PublishAsync(corridor, ChangeBodies.EarlyEnd(corridor.Stop("PLG")));

        // Put the row back as it would have been written before pickup times existed.
        var oldPayload = JsonSerializer.Serialize(new
        {
            event_id = $"train-change:{changeId}",
            journey_id = corridor.JourneyId,
            journey_version = 1,
            change_kind = "EARLY_END",
            action = "PUBLISH",
            confirmed_delay_minutes = (int?)null,
            order_closed = false,
            blocked_pickup_stop_ids = new[] { corridor.Stop("PDN") },
        });
        await api.WithDbAsync(async db =>
        {
            var row = db.OutboxMessages.Single(m => m.ServiceChangeId == changeId);
            row.Payload = oldPayload;
            row.PayloadVersion = PayloadVersions.EligibilityOnly;
            await db.SaveChangesAsync();
        });

        var upgraded = 0;
        await api.WithScopeAsync(async services => upgraded = await services.GetRequiredService<OutboxUpgrader>().UpgradeAllAsync(default));

        Assert.True(upgraded >= 1);
        var row = Assert.Single(await api.OutboxOfAsync(corridor));
        Assert.Equal((PayloadVersions.WithPickupTimes, DeliveryStates.Pending), (row.PayloadVersion, row.DeliveryState));
        Assert.Equal($"train-change:{changeId}", row.EventId);
        var rebuilt = JsonDocument.Parse(row.Payload).RootElement;
        Assert.Equal(1, rebuilt.GetProperty("journey").GetProperty("version").GetInt32());
        Assert.Equal(corridor.Stop("PDN"), rebuilt.GetProperty("journey").GetProperty("blockedPickupStopIds")[0].GetGuid());
        Assert.NotEmpty(rebuilt.GetProperty("stops").EnumerateArray());
        // Upgrading is not delivering: RailBites still holds the order as accepted.
        Assert.Equal("ACCEPTED", (await ProviderAsync(order)).GetProperty("status").GetString());

        await DeliverAllAsync();

        Assert.Equal(DeliveryStates.Delivered, Assert.Single(await api.OutboxOfAsync(corridor)).DeliveryState);
        Assert.Equal("CANCELLED", (await ProviderAsync(order)).GetProperty("status").GetString());
    }

    [RequiresDatabaseFact]
    public async Task An_old_format_message_that_cannot_be_rebuilt_is_kept_as_failed_not_dropped_or_delivered()
    {
        var corridor = await api.RunningCorridorAsync();
        await api.PublishAsync(corridor, ChangeBodies.Delay(5));
        await api.WithDbAsync(async db =>
        {
            var row = db.OutboxMessages.Single(m => db.ServiceChanges.Any(c => c.Id == m.ServiceChangeId && c.JourneyId == corridor.JourneyId));
            row.Payload = """{"event_id":"x"}""";
            row.PayloadVersion = PayloadVersions.EligibilityOnly;
            await db.SaveChangesAsync();
        });

        await DeliverAllAsync();

        var row = Assert.Single(await api.OutboxOfAsync(corridor));
        Assert.Equal((DeliveryStates.Failed, OutboxUpgrader.Unreadable), (row.DeliveryState, row.LastError));
        Assert.Null(row.DeliveredAt);
    }

    [RequiresDatabaseFact]
    public async Task A_permanent_refusal_is_kept_and_retried_slowly_not_dropped()
    {
        var corridor = await api.RunningCorridorAsync();
        await api.PublishAsync(corridor, ChangeBodies.Delay(5));
        await api.WithDbAsync(async db =>
        {
            var row = db.OutboxMessages.Single(m => db.ServiceChanges.Any(c => c.Id == m.ServiceChangeId && c.JourneyId == corridor.JourneyId));
            // A body RailBites will refuse as malformed, whatever the number of retries.
            row.Payload = row.Payload.Replace("\"DELAY\"", "\"NOT_A_KIND\"");
            await db.SaveChangesAsync();
        });

        await DeliverAllAsync();

        var row = Assert.Single(await api.OutboxOfAsync(corridor));
        Assert.Equal((DeliveryStates.Failed, "INVALID_REQUEST"), (row.DeliveryState, row.LastError));
        Assert.True(row.NextAttemptAt - api.Clock.Now >= TimeSpan.FromMinutes(5));
        await api.RunOutboxPassAsync(); // not due again, so this pass leaves it alone
        Assert.Equal(1, Assert.Single(await api.OutboxOfAsync(corridor)).AttemptCount);
    }
}
