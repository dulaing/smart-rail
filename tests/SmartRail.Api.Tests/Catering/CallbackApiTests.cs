using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.Metrics;
using SmartRail.Api.Modules.Catering;
using SmartRail.Api.Modules.Catering.Callbacks;
using SmartRail.Api.Modules.Catering.Orders;
using SmartRail.Api.Tests.Operations;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Catering;

public class CallbackApiTests(CallbackApi api) : IClassFixture<CallbackApi>
{
    private async Task<(string Passenger, Guid Id, Corridor Corridor)> PlaceAsync()
    {
        var subject = $"callback-{Guid.NewGuid():N}";
        await api.AddPassengerAsync(subject);
        var corridor = await api.RunningCorridorAsync();
        var response = await api.OrderAsync(subject, Guid.NewGuid().ToString(), corridor, "PLG", "polgahawela-junction", ("plg-vegetable-roll", 1));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (subject, (await response.JsonAsync()).GetProperty("id").GetGuid(), corridor);
    }

    private async Task<string> BodyAsync(Guid id, int sequence, string status = "READY", string? eventId = null)
    {
        var link = await api.LinkAsync(id);
        return JsonSerializer.Serialize(new
        {
            eventId = eventId ?? $"{link.ProviderOrderRef}-test-{sequence}", orderLinkId = id,
            providerOrderRef = link.ProviderOrderRef, status, statusSequence = sequence, reasonCode = (string?)null,
            pickupTime = new { scheduledArrival = link.ScheduledArrival, estimatedArrival = link.EstimatedArrival, etaEvidenceAt = link.EtaEvidenceAt },
            occurredAt = api.Clock.Now,
        });
    }

    private Task<HttpResponseMessage> SendAsync(string body, string? secret = GrilledProcess.CallbackSecret, long? timestamp = null, string? signature = null)
    {
        var seconds = timestamp ?? api.Clock.Now.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? ""));
        var value = signature ?? "v1=" + Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{seconds}.{body}")));
        var request = new HttpRequestMessage(HttpMethod.Post, CallbackEndpoints.Path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add(CallbackSignature.TimestampHeader, seconds.ToString());
        request.Headers.Add(CallbackSignature.SignatureHeader, value);
        return api.Client.SendAsync(request);
    }

    private async Task<int> ReceiptsAsync(Guid id)
    {
        var count = 0;
        await api.WithDbAsync(async db => count = await db.WebhookReceipts.CountAsync(r => r.OrderLinkId == id));
        return count;
    }

    [RequiresDatabaseFact]
    public async Task Authentication_precedes_replay_checks_and_bounds_raw_body()
    {
        long rejected = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "catering.callbacks.rejected") meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref rejected, value));
        listener.Start();
        var (_, id, _) = await PlaceAsync();
        var body = await BodyAsync(id, 2);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(body, "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(body, timestamp: api.Clock.Now.ToUnixTimeSeconds() - 301)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(body + " ", signature: "v1=bad")).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await SendAsync(new string('x', CallbackEndpoints.MaxBodyBytes + 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync("{}")).StatusCode);
        Assert.Equal(1, await ReceiptsAsync(id));
        Assert.Equal(3, rejected);
        var configuration = api.Factory.Services.GetRequiredService<IConfiguration>();
        var secret = configuration[CallbackEndpoints.SecretSetting];
        try
        {
            configuration[CallbackEndpoints.SecretSetting] = "";
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(body)).StatusCode);
        }
        finally { configuration[CallbackEndpoints.SecretSetting] = secret; }
    }

    [RequiresDatabaseFact]
    public async Task Duplicates_old_sequences_and_conflicting_event_content_cannot_regress_an_order()
    {
        var (_, id, _) = await PlaceAsync();
        var newer = await BodyAsync(id, 3, "COLLECTED");
        Assert.Equal("APPLIED", (await (await SendAsync(newer)).JsonAsync()).GetProperty("result").GetString());
        Assert.Equal("DUPLICATE", (await (await SendAsync(newer)).JsonAsync()).GetProperty("result").GetString());
        var old = await BodyAsync(id, 2);
        Assert.Equal("STALE", (await (await SendAsync(old)).JsonAsync()).GetProperty("result").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(newer.Replace("COLLECTED", "READY"))).StatusCode);
        Assert.Equal(("COLLECTED", 3), ((await api.LinkAsync(id)).ProviderStatus, (await api.LinkAsync(id)).StatusSequence));
        Assert.Equal(2, await ReceiptsAsync(id));
    }

    [RequiresDatabaseFact]
    public async Task Concurrent_duplicate_and_out_of_order_callbacks_commit_one_receipt_each()
    {
        var (_, id, _) = await PlaceAsync();
        var ready = await BodyAsync(id, 2);
        var collected = await BodyAsync(id, 3, "COLLECTED");
        var responses = await Task.WhenAll(SendAsync(ready), SendAsync(ready), SendAsync(collected), SendAsync(collected));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(2, await ReceiptsAsync(id));
        Assert.Equal(("COLLECTED", 3), ((await api.LinkAsync(id)).ProviderStatus, (await api.LinkAsync(id)).StatusSequence));
    }

    [RequiresDatabaseFact]
    public async Task Receipt_and_order_update_roll_back_together_if_commit_fails()
    {
        var (_, id, _) = await PlaceAsync();
        var body = await BodyAsync(id, 999);
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION catering.fail_test_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.status_sequence = 999 THEN RAISE EXCEPTION 'injected receipt commit failure'; END IF; RETURN NEW; END $$;
            CREATE CONSTRAINT TRIGGER fail_test_receipt AFTER INSERT ON catering.webhook_receipts
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION catering.fail_test_receipt();
            """));
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await SendAsync(body)).StatusCode);
            Assert.Equal(0, await ReceiptsAsync(id));
            Assert.Equal(1, (await api.LinkAsync(id)).StatusSequence);
        }
        finally
        {
            await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_test_receipt ON catering.webhook_receipts; DROP FUNCTION catering.fail_test_receipt();"));
        }
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(body)).StatusCode);
        Assert.Equal(1, await ReceiptsAsync(id));
        Assert.Equal(999, (await api.LinkAsync(id)).StatusSequence);
    }

    [RequiresDatabaseFact]
    public async Task Real_callbacks_update_pickup_times_and_the_passengers_collected_card()
    {
        var (passenger, id, corridor) = await PlaceAsync();
        await api.RunCallbacksAsync(); // initial ACCEPTED, already known from placement
        await api.PublishAsync(corridor, ChangeBodies.Delay(20));
        await api.RunOutboxPassAsync();
        await api.RunCallbacksAsync();
        Assert.Equal(corridor.At("11:20"), (await api.LinkAsync(id)).EstimatedArrival);
        var provider = (await api.ProviderOrderAsync(id))!.Value;
        var providerId = provider.GetProperty("id").GetString();
        (await api.Grilled.KioskAsync("polgahawela-junction", GrilledProcess.PolgahawelaKioskKey, HttpMethod.Post, $"/{providerId}/ready")).EnsureSuccessStatusCode();
        (await api.Grilled.KioskAsync("polgahawela-junction", GrilledProcess.PolgahawelaKioskKey, HttpMethod.Post,
            $"/{providerId}/collected", JsonSerializer.Serialize(new { pickupCode = provider.GetProperty("pickupCode").GetString() }))).EnsureSuccessStatusCode();
        await api.RunCallbacksAsync();
        var card = await (await api.SendAsync(passenger, HttpMethod.Get, $"{OrderEndpoints.Path}/{id}")).JsonAsync();
        Assert.Equal("COLLECTED", card.GetProperty("status").GetString());
        Assert.Equal(provider.GetProperty("pickupCode").GetString(), card.GetProperty("pickupCode").GetString());
        Assert.Equal(4, await ReceiptsAsync(id));
    }

    [RequiresDatabaseFact]
    public async Task A_callback_before_the_placement_response_hydrates_the_order_and_the_late_response_cannot_regress_it()
    {
        var passenger = $"early-callback-{Guid.NewGuid():N}";
        await api.AddPassengerAsync(passenger);
        var corridor = await api.RunningCorridorAsync();
        api.Proxy.BeforeResponse = async request =>
        {
            if (request.Method != HttpMethod.Put) return;
            var id = Guid.Parse(request.RequestUri!.Segments.Last());
            var provider = (await api.ProviderOrderAsync(id))!.Value;
            (await api.Grilled.KioskAsync("polgahawela-junction", GrilledProcess.PolgahawelaKioskKey,
                HttpMethod.Post, $"/{provider.GetProperty("id").GetString()}/ready")).EnsureSuccessStatusCode();
            await api.RunCallbacksAsync();
        };
        try
        {
            var response = await api.OrderAsync(passenger, Guid.NewGuid().ToString(), corridor, "PLG", "polgahawela-junction", ("plg-vegetable-roll", 1));
            var card = await response.JsonAsync();
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("READY", card.GetProperty("status").GetString());
            Assert.Equal(2, card.GetProperty("statusSequence").GetInt32());
            Assert.False(string.IsNullOrEmpty(card.GetProperty("pickupCode").GetString()));
            var link = await api.LinkAsync(card.GetProperty("id").GetGuid());
            Assert.Null(link.RequestPayload);
            Assert.Equal(2, await ReceiptsAsync(link.Id));
        }
        finally { api.Proxy.BeforeResponse = null; }
    }

    [RequiresDatabaseFact]
    public async Task A_pending_link_is_not_acknowledged_until_its_private_order_details_can_be_reconciled()
    {
        var passenger = $"pending-callback-{Guid.NewGuid():N}";
        await api.AddPassengerAsync(passenger);
        var corridor = await api.RunningCorridorAsync();
        api.Proxy.Mode = ProxyMode.DropResponse;
        try
        {
            var response = await api.OrderAsync(passenger, Guid.NewGuid().ToString(), corridor, "PLG", "polgahawela-junction", ("plg-vegetable-roll", 1));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var id = (await response.JsonAsync()).GetProperty("id").GetGuid();
            api.Proxy.Mode = ProxyMode.ServiceUnavailable;
            await api.RunCallbacksAsync();
            Assert.Equal(0, await ReceiptsAsync(id));
            Assert.Equal(PlacementStates.Pending, (await api.LinkAsync(id)).PlacementState);
            api.Proxy.Mode = ProxyMode.Forward;
            await api.AdvanceAsync(TimeSpan.FromSeconds(3));
            await api.RunCallbacksAsync();
            var confirmed = await api.LinkAsync(id);
            Assert.Equal(PlacementStates.Confirmed, confirmed.PlacementState);
            Assert.NotNull(confirmed.PickupCode);
            Assert.Null(confirmed.RequestPayload);
            Assert.Equal(1, await ReceiptsAsync(id));
        }
        finally { api.Proxy.Mode = ProxyMode.Forward; }
    }

    [RequiresDatabaseFact]
    public async Task Real_train_cancellation_callbacks_preserve_the_reason_and_do_not_revive_orders_on_withdrawal()
    {
        var (_, id, corridor) = await PlaceAsync();
        var changeId = await api.PublishAsync(corridor, ChangeBodies.Cancel());
        await api.RunOutboxPassAsync();
        await api.RunCallbacksAsync();
        var cancelled = await api.LinkAsync(id);
        Assert.Equal(("CANCELLED", "TRAIN_CANCELLED"), (cancelled.ProviderStatus, cancelled.CancellationReason));
        await api.PublishAsync(corridor, ChangeBodies.Withdraw(changeId));
        await api.RunOutboxPassAsync();
        await api.RunCallbacksAsync();
        Assert.Equal(cancelled.StatusSequence, (await api.LinkAsync(id)).StatusSequence);
        Assert.Equal("CANCELLED", (await api.LinkAsync(id)).ProviderStatus);
    }

    [RequiresDatabaseFact]
    public async Task Lost_acknowledgement_and_both_service_restarts_redeliver_without_duplicate_effects()
    {
        var (_, id, _) = await PlaceAsync();
        var stock = await api.ProviderStockAsync("polgahawela-junction", "plg-vegetable-roll");
        api.Bridge.Mode = ProxyMode.DropResponse;
        try
        {
            await api.RunCallbacksAsync();
            Assert.Equal(1, await ReceiptsAsync(id));
            api.Bridge.Mode = ProxyMode.ServiceUnavailable;
            await api.RestartHostAsync();
            await api.Grilled.RestartAsync();
            await api.AdvanceAsync(TimeSpan.FromSeconds(3));
            await api.RunCallbacksAsync();
            Assert.Equal(1, await ReceiptsAsync(id));
            api.Bridge.Mode = ProxyMode.Forward;
            await api.AdvanceAsync(TimeSpan.FromSeconds(5));
            await api.RunCallbacksAsync();
            var forwarded = api.Bridge.Forwarded;
            await api.AdvanceAsync(TimeSpan.FromSeconds(10));
            await api.RunCallbacksAsync();
            Assert.Equal(forwarded, api.Bridge.Forwarded); // acknowledged queue is now empty
            Assert.Equal(1, await ReceiptsAsync(id));
            Assert.Equal(stock, await api.ProviderStockAsync("polgahawela-junction", "plg-vegetable-roll"));
        }
        finally { api.Bridge.Mode = ProxyMode.Forward; }
    }

    [RequiresDatabaseFact]
    public async Task Passage_requires_its_own_service_key_and_no_show_reads_real_passage()
    {
        var (_, id, corridor) = await PlaceAsync();
        var path = $"/internal/catering/journeys/{corridor.JourneyId}/stops/{corridor.Stop("PLG")}/passage";
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.GetAsync(path)).StatusCode);
        using var wrong = new HttpRequestMessage(HttpMethod.Get, path);
        wrong.Headers.Add(PassageEndpoints.KeyHeader, GrilledProcess.ServiceKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.SendAsync(wrong)).StatusCode);
        async Task<string> PassageAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add(PassageEndpoints.KeyHeader, CallbackApi.InboundKey);
            return (await (await api.Client.SendAsync(request)).JsonAsync()).GetProperty("state").GetString()!;
        }
        Assert.Equal("AHEAD", await PassageAsync());
        await api.AdvanceAsync(TimeSpan.FromMinutes(5));
        Assert.Equal("UNKNOWN", await PassageAsync());
        var provider = (await api.ProviderOrderAsync(id))!.Value;
        var suffix = $"/{provider.GetProperty("id").GetString()}/no-show";
        Assert.Equal(HttpStatusCode.Conflict, (await api.Grilled.KioskAsync("polgahawela-junction", GrilledProcess.PolgahawelaKioskKey, HttpMethod.Post, suffix)).StatusCode);
        await api.SetNowAsync(corridor.At("11:05"));
        await api.ObserveAsync(corridor, "PLG", 10, api.Clock.Now);
        (await api.Grilled.KioskAsync("polgahawela-junction", GrilledProcess.PolgahawelaKioskKey, HttpMethod.Post, suffix)).EnsureSuccessStatusCode();
        await api.RunCallbacksAsync();
        Assert.Equal("NO_SHOW", (await api.LinkAsync(id)).ProviderStatus);
    }
}
