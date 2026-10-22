using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SmartRail.Api.Modules.Catering.Callbacks;
using SmartRail.Api.Modules.Catering.Outbox;
using SmartRail.Api.Tests.Operations;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Catering;

// The local failure-and-recovery drill, checked against both real services: provider outage as a
// degraded dependency (never down), backlog numbers that rise and drain, the provider-owned cutoff
// changed without touching SmartRail, retry and rejection metrics, and one trace across the boundary.
public class DrillApiTests(CallbackApi api) : IClassFixture<CallbackApi>
{
    private static int _subjects;

    private async Task<string> NewPassengerAsync()
    {
        var subject = $"drill-passenger-{Interlocked.Increment(ref _subjects)}";
        await api.AddPassengerAsync(subject);
        return subject;
    }

    private static string Key() => Guid.NewGuid().ToString("N");

    private async Task<JsonElement> ReadyAsync(HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await api.Client.GetAsync("/health/ready");
        Assert.Equal(expected, response.StatusCode);
        return await response.JsonAsync();
    }

    private static JsonElement Entry(JsonElement ready, string name) =>
        ready.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);

    private Task<HttpResponseMessage> SetOutageAsync(bool active) =>
        api.Grilled.Http.PutAsJsonAsync("/internal/dev/outage", new { active });

    // Brings both services back to a clean, caught-up state between drills.
    private async Task DrainAsync()
    {
        for (var i = 0; i < 6; i++)
        {
            await api.AdvanceAsync(TimeSpan.FromSeconds(70));
            await api.RunPlacementPassAsync();
            await api.RunOutboxPassAsync();
            await api.RunCallbacksAsync();
        }
    }

    [RequiresDatabaseFact]
    public async Task Readiness_is_healthy_when_nothing_is_wrong_and_liveness_checks_nothing()
    {
        var ready = await ReadyAsync();

        Assert.Equal("Healthy", ready.GetProperty("status").GetString());
        var catering = Entry(ready, "catering");
        Assert.Equal("Healthy", catering.GetProperty("status").GetString());
        Assert.Equal("Ready", catering.GetProperty("data").GetProperty("provider").GetString());
        Assert.Equal(0, catering.GetProperty("data").GetProperty("outboxPending").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await api.Client.GetAsync("/health/live")).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task A_stopped_provider_makes_the_dependency_degraded_not_the_api_down_and_train_reads_keep_working()
    {
        var corridor = await api.RunningCorridorAsync();
        await api.Grilled.StopAsync();
        try
        {
            var ready = await ReadyAsync(); // still 200: the API can serve train information

            Assert.Equal("Degraded", ready.GetProperty("status").GetString());
            var catering = Entry(ready, "catering");
            Assert.Equal("Degraded", catering.GetProperty("status").GetString());
            Assert.Contains("unreachable", catering.GetProperty("description").GetString());
            Assert.Equal("Unreachable", catering.GetProperty("data").GetProperty("provider").GetString());
            Assert.Equal("Healthy", Entry(ready, "smartrail-db").GetProperty("status").GetString());

            Assert.Equal(HttpStatusCode.OK, (await api.Client.GetAsync("/health/live")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await api.Client.GetAsync($"/api/journeys/{corridor.JourneyId}/running")).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await api.Client.GetAsync($"/api/journeys/{corridor.JourneyId}/collection-options")).StatusCode);
        }
        finally
        {
            await api.Grilled.StartAsync();
            await api.Grilled.SetNowAsync(api.Clock.Now);
        }

        Assert.Equal("Healthy", (await ReadyAsync()).GetProperty("status").GetString());
    }

    [RequiresDatabaseFact]
    public async Task The_development_outage_switch_degrades_the_dependency_and_its_restore_route_ends_it()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();

        Assert.Equal(HttpStatusCode.OK, (await SetOutageAsync(true)).StatusCode);
        try
        {
            var ready = await ReadyAsync();
            Assert.Equal("Degraded", Entry(ready, "catering").GetProperty("status").GetString());
            Assert.Equal("NotReady", Entry(ready, "catering").GetProperty("data").GetProperty("provider").GetString());

            // The provider answers 503 to everything but health and the restore route.
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await api.Grilled.Http.GetAsync("/v1/collection-points")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await api.Grilled.Http.GetAsync("/health/live")).StatusCode);

            // An order placed now is kept, not lost or refused.
            var response = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var id = (await response.JsonAsync()).GetProperty("id").GetGuid();

            // The switch is reachable while the provider is "down", and ending it needs no restart.
            Assert.Equal(HttpStatusCode.OK, (await SetOutageAsync(false)).StatusCode);
            await api.AdvanceAsync(TimeSpan.FromSeconds(5));
            await api.RunPlacementPassAsync();
            Assert.Equal("CONFIRMED", (await api.LinkAsync(id)).PlacementState);
        }
        finally
        {
            await SetOutageAsync(false);
        }

        await DrainAsync();
        Assert.Equal("Healthy", (await ReadyAsync()).GetProperty("status").GetString());
    }

    [RequiresDatabaseFact]
    public async Task The_switch_needs_the_service_key()
    {
        var anonymous = await new HttpClient().PutAsJsonAsync($"{api.Grilled.BaseUrl}/internal/dev/outage", new { active = true });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Grilled.Http.GetAsync("/v1/collection-points")).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task Backlog_diagnostics_rise_while_the_provider_is_down_and_drain_after_it_returns()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var order = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
        Assert.Equal(HttpStatusCode.Created, order.StatusCode);
        await DrainAsync();
        await api.ObserveAsync(corridor, "GPH", 900, api.Clock.Now.AddSeconds(-5)); // the drain moved time on

        await SetOutageAsync(true);
        try
        {
            await api.PublishAsync(corridor, ChangeBodies.Delay(10));
            var pending = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
            Assert.Equal(HttpStatusCode.Accepted, pending.StatusCode);
            await api.RunOutboxPassAsync();
            await api.AdvanceAsync(TimeSpan.FromMinutes(3));
            await api.ObserveAsync(corridor, "GPH", 900, api.Clock.Now.AddSeconds(-5));

            var catering = Entry(await ReadyAsync(), "catering");
            var data = catering.GetProperty("data");
            Assert.Equal("Degraded", catering.GetProperty("status").GetString());
            Assert.True(data.GetProperty("outboxPending").GetInt32() >= 1);
            Assert.True(data.GetProperty("oldestOutboxSeconds").GetInt32() >= 180);
            Assert.True(data.GetProperty("ordersPending").GetInt32() >= 1);
            Assert.True(data.GetProperty("oldestPendingOrderSeconds").GetInt32() >= 180);
            Assert.Contains("waiting", catering.GetProperty("description").GetString());
            // Counts and ages only: nothing personal or secret.
            Assert.DoesNotContain("pickup", catering.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await SetOutageAsync(false);
        }

        await DrainAsync();
        var drained = Entry(await ReadyAsync(), "catering");
        Assert.Equal("Healthy", drained.GetProperty("status").GetString());
        Assert.Equal(0, drained.GetProperty("data").GetProperty("outboxPending").GetInt32());
        Assert.Equal(0, drained.GetProperty("data").GetProperty("ordersPending").GetInt32());
    }

    [RequiresDatabaseFact]
    public async Task A_refused_train_change_is_reported_for_correction_not_hidden()
    {
        var corridor = await api.RunningCorridorAsync();
        await api.PublishAsync(corridor, ChangeBodies.Delay(5));
        await api.WithDbAsync(async db =>
        {
            var row = db.OutboxMessages.Single(m => db.ServiceChanges.Any(c => c.Id == m.ServiceChangeId && c.JourneyId == corridor.JourneyId));
            row.Payload = row.Payload.Replace("\"DELAY\"", "\"NOT_A_KIND\"");
            await db.SaveChangesAsync();
        });
        await api.AdvanceAsync(TimeSpan.FromSeconds(1));
        await api.RunOutboxPassAsync();

        var catering = Entry(await ReadyAsync(), "catering");
        Assert.Equal("Degraded", catering.GetProperty("status").GetString());
        Assert.True(catering.GetProperty("data").GetProperty("outboxFailed").GetInt32() >= 1);
        Assert.Contains("need correction", catering.GetProperty("description").GetString());

        // Correcting the cause lets the same message through; nothing was dropped.
        await api.WithDbAsync(async db =>
        {
            var row = db.OutboxMessages.Single(m => db.ServiceChanges.Any(c => c.Id == m.ServiceChangeId && c.JourneyId == corridor.JourneyId));
            row.Payload = row.Payload.Replace("\"NOT_A_KIND\"", "\"DELAY\"");
            row.NextAttemptAt = api.Clock.Now;
            row.DeliveryState = DeliveryStates.Pending;
            await db.SaveChangesAsync();
        });
        await api.RunOutboxPassAsync();
        Assert.Equal(DeliveryStates.Delivered, Assert.Single(await api.OutboxOfAsync(corridor)).DeliveryState);
    }

    [RequiresDatabaseFact]
    public async Task Changing_the_providers_cutoff_to_45_takes_effect_without_touching_SmartRail()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var peradeniyaEta = corridor.At("11:50");
        await api.SetNowAsync(peradeniyaEta.AddMinutes(-40)); // 11:10, between the 30 and 45 minute cutoffs
        await api.ObserveAsync(corridor, "PLG", 300, api.Clock.Now.AddSeconds(-5));

        async Task<JsonElement> OptionAsync() => (await (await api.Client.GetAsync($"/api/journeys/{corridor.JourneyId}/collection-options")).JsonAsync())
            .GetProperty("options").EnumerateArray().Single(o => o.GetProperty("collectionPoint").GetProperty("id").GetString() == "peradeniya-junction");

        Assert.Equal(30, (await OptionAsync()).GetProperty("collectionPoint").GetProperty("cutoffMinutes").GetInt32());
        var open = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
        Assert.Equal(HttpStatusCode.Created, open.StatusCode);

        // The provider changes its own policy; SmartRail is neither restarted nor reconfigured.
        var request = new HttpRequestMessage(HttpMethod.Put, "/v1/admin/collection-points/peradeniya-junction/cutoff")
        {
            Content = JsonContent.Create(new { cutoffMinutes = 45 }),
        };
        request.Headers.Add("X-Grilled-Admin-Key", GrilledProcess.AdminKey);
        Assert.Equal(HttpStatusCode.OK, (await api.Grilled.Http.SendAsync(request)).StatusCode);

        var option = await OptionAsync();
        Assert.Equal(45, option.GetProperty("collectionPoint").GetProperty("cutoffMinutes").GetInt32());
        Assert.Equal(peradeniyaEta.AddMinutes(-45), option.GetProperty("orderBy").GetDateTimeOffset());
        var closed = await api.OrderAsync(passenger, Key(), corridor, "PDN", "peradeniya-junction", ("pdn-fish-bun", 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, closed.StatusCode);
        Assert.Equal("CUTOFF_PASSED", (await closed.JsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await api.SendAsync(passenger, HttpMethod.Get, "/api/food/orders")).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task The_cutoff_route_refuses_the_service_key()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/v1/admin/collection-points/peradeniya-junction/cutoff")
        {
            Content = JsonContent.Create(new { cutoffMinutes = 45 }),
        };
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Grilled.Http.SendAsync(request)).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task Retries_and_rejected_callbacks_are_counted()
    {
        var corridor = await api.RunningCorridorAsync();
        var counts = new Dictionary<string, long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == CateringMetrics.MeterName) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var label = string.Join(",", tags.ToArray().Select(t => $"{t.Key}={t.Value}"));
            lock (counts) counts[$"{instrument.Name}[{label}]"] = counts.GetValueOrDefault($"{instrument.Name}[{label}]") + value;
        });
        listener.Start();

        // A delivery that fails then succeeds is one retry and one delivered attempt.
        await api.PublishAsync(corridor, ChangeBodies.Delay(5));
        api.Proxy.Mode = ProxyMode.ServiceUnavailable;
        try
        {
            await api.AdvanceAsync(TimeSpan.FromSeconds(1));
            await api.RunOutboxPassAsync();
        }
        finally
        {
            api.Proxy.Mode = ProxyMode.Forward;
        }

        await api.AdvanceAsync(TimeSpan.FromSeconds(10));
        await api.RunOutboxPassAsync();

        // A callback with a bad signature is refused and counted by reason.
        var forged = new HttpRequestMessage(HttpMethod.Post, CallbackEndpoints.Path)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        forged.Headers.Add(CallbackSignature.TimestampHeader, api.Clock.Now.ToUnixTimeSeconds().ToString());
        forged.Headers.Add(CallbackSignature.SignatureHeader, "v1=" + new string('0', 64));
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.SendAsync(forged)).StatusCode);

        listener.RecordObservableInstruments();
        lock (counts)
        {
            Assert.True(counts.GetValueOrDefault("catering.outbox.attempts[result=retry]") >= 1, string.Join(" | ", counts));
            Assert.True(counts.GetValueOrDefault("catering.outbox.attempts[result=delivered]") >= 1);
            Assert.True(counts.GetValueOrDefault("catering.callbacks.rejected[reason=BadSignature]") >= 1);
        }
    }

    [RequiresDatabaseFact]
    public async Task One_trace_runs_from_the_passengers_request_through_Grilled_and_back_in_the_callback()
    {
        var passenger = await NewPassengerAsync();
        var corridor = await api.RunningCorridorAsync();
        var seen = new List<(string Path, string Trace)>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                lock (seen) seen.Add((activity.GetTagItem("url.path") as string ?? "", activity.TraceId.ToHexString()));
            },
        };
        ActivitySource.AddActivityListener(listener);
        string Traced(string trace) => $"00-{trace}-00f067aa0ba902b7-01";
        var placeTrace = "4bf92f3577b34da6a3ce929d0e0e4736";
        var kioskTrace = "0af7651916cd43dd8448eb211c80319c";

        // The passenger's request arrives with a trace; SmartRail passes it on to Grilled.
        var order = new HttpRequestMessage(HttpMethod.Post, "/api/food/orders")
        {
            Content = JsonContent.Create(new
            {
                clientRequestKey = Key(),
                collectionPointId = "peradeniya-junction",
                journeyId = corridor.JourneyId,
                pickupStopId = corridor.Stop("PDN"),
                items = new[] { new { menuItemId = "pdn-fish-bun", quantity = 1 } },
            }),
        };
        order.Headers.Add("traceparent", Traced(placeTrace));
        order.Headers.Authorization = new("Bearer", api.Token(passenger));
        var placed = await api.Client.SendAsync(order);
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        Assert.Contains(api.Proxy.Traceparents, t => t is not null && t.Split('-')[1] == placeTrace);

        // Grilled tells SmartRail, and that callback joins the same trace.
        await api.RunCallbacksAsync();
        lock (seen)
        {
            Assert.Contains(seen, s => s.Path == CallbackEndpoints.Path && s.Trace == placeTrace);
        }

        // A kiosk action has its own trace, and the callback it causes carries that one.
        var providerId = (await api.ProviderOrderAsync((await placed.JsonAsync()).GetProperty("id").GetGuid()))!.Value.GetProperty("id").GetString();
        var ready = new HttpRequestMessage(HttpMethod.Post, $"/v1/kiosk/collection-points/peradeniya-junction/orders/{providerId}/ready");
        ready.Headers.Add("X-Grilled-Kiosk-Key", GrilledProcess.PeradeniyaKioskKey);
        ready.Headers.Add("traceparent", Traced(kioskTrace));
        // A client of its own that sends the trace header exactly as written; the shared one would
        // replace it with the test process's own trace, as an instrumented kiosk client would.
        using var kioskClient = new HttpClient(new SocketsHttpHandler { ActivityHeadersPropagator = null }) { BaseAddress = api.Grilled.Http.BaseAddress };
        Assert.Equal(HttpStatusCode.OK, (await kioskClient.SendAsync(ready)).StatusCode);
        var queued = await api.Grilled.Http.GetStringAsync("/internal/test/callbacks");
        await api.RunCallbacksAsync();
        lock (seen)
        {
            Assert.True(seen.Any(s => s.Path == CallbackEndpoints.Path && s.Trace == kioskTrace), queued + " || " + string.Join(" | ", seen.Where(s => s.Path.StartsWith("/internal")).Select(s => s.Trace)));
        }
    }
}
