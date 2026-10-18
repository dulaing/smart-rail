using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Catering.Orders;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Modules.Rail.Timetable;
using SmartRail.Api.Tests.Access;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Catering;

// One dated run of the corridor train SIM-3001 (Fort 09:00 ... Polgahawela 11:00 ... Peradeniya
// 11:50, Colombo time) with the IDs of its stops.
public sealed record Corridor(Guid JourneyId, DateOnly Date, IReadOnlyList<(string Code, Guid StopId)> Stops)
{
    public Guid Stop(string code) => Stops.Single(s => s.Code == code).StopId;

    public int IndexOf(string code) => Stops.ToList().FindIndex(s => s.Code == code);

    // A Colombo wall-clock time on the journey's service date, as an instant.
    public DateTimeOffset At(string hhmm) => ColomboTime.ToInstant(Date, TimeOnly.Parse(hhmm), 0);
}

// The SmartRail API wired to a real RailBites process through a proxy that can lose messages, with
// one hand-moved clock shared by both services. Needs a disposable PostgreSQL server and a built
// services/railbites. Each test uses its own corridor journey, because they share one database.
public class CateringApi : AccessApi
{
    public const string ObservationKey = "test-observation-key";

    private static int _nextDay;

    private RailBitesProcess? _railBites;

    public RailBitesProcess RailBites => _railBites!;

    public FlakyProxy Proxy { get; private set; } = null!;

    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero));

    protected virtual void ConfigureRailBites(RailBitesProcess railBites)
    {
    }

    public override async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RequiresDatabaseFactAttribute.Variable)))
        {
            return;
        }

        _railBites = new RailBitesProcess();
        ConfigureRailBites(_railBites);
        await _railBites.StartAsync();
        Proxy = await FlakyProxy.StartAsync(_railBites.BaseUrl);
        await base.InitializeAsync();
    }

    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        base.ConfigureHost(builder);
        builder.UseSetting(ServiceKeyFilter.ConfigurationKey, ObservationKey);
        builder.UseSetting("RailBites:BaseUrl", Proxy.BaseUrl);
        builder.UseSetting("RailBites:ServiceKey", RailBitesProcess.ServiceKey);
        builder.UseSetting("RailBites:TimeoutSeconds", "5");

        // The tests run the retry pass by hand, at times they choose.
        builder.UseSetting("Catering:Placement:WorkerEnabled", "false");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (Proxy is not null)
        {
            await Proxy.DisposeAsync();
        }

        if (_railBites is not null)
        {
            await _railBites.DisposeAsync();
        }
    }

    // Moves SmartRail's clock and RailBites' clock to the same instant.
    public async Task SetNowAsync(DateTimeOffset now)
    {
        // The API writes the clock's value to PostgreSQL, which accepts only a UTC offset.
        now = now.ToUniversalTime();
        Clock.Set(now);
        await RailBites.SetNowAsync(now);
    }

    public Task AdvanceAsync(TimeSpan by) => SetNowAsync(Clock.Now + by);

    // A new dated run of SIM-3001 that no other test touches. Its date is past the seeded week, so
    // it cannot collide with seeded journeys.
    public async Task<Corridor> NewCorridorAsync()
    {
        var date = new DateOnly(2026, 11, 1).AddDays(Interlocked.Increment(ref _nextDay));
        var journeyId = Guid.NewGuid();
        var stops = new List<(string, Guid)>();
        await WithDbAsync(async db =>
        {
            var pattern = await db.ServicePatterns.Include(p => p.Stops).SingleAsync(p => p.TrainNumber == "SIM-3001");
            db.Journeys.Add(new ScheduledJourney { Id = journeyId, PatternId = pattern.Id, Pattern = pattern, ServiceDate = date });
            db.JourneyVersions.Add(new JourneyChangeVersion { JourneyId = journeyId, Version = 0, UpdatedAt = Clock.Now });
            await db.SaveChangesAsync();

            var codes = await db.Stations.ToDictionaryAsync(s => s.Id, s => s.Code);
            stops.AddRange(pattern.Stops.OrderBy(s => s.Sequence).Select(s => (codes[s.StationId], s.Id)));
        });
        return new Corridor(journeyId, date, stops);
    }

    // Reports the train between two neighbouring stops of the corridor, observed at `observedAt`.
    public async Task ObserveAsync(Corridor corridor, string fromCode, int progress, DateTimeOffset observedAt, int delaySeconds = 0)
    {
        var from = corridor.IndexOf(fromCode);
        var request = new HttpRequestMessage(HttpMethod.Post, RunningEndpoints.ObservationsPath)
        {
            Content = JsonContent.Create(new
            {
                sourceEventId = $"evt-{Guid.NewGuid():N}",
                journeyId = corridor.JourneyId,
                fromStopId = corridor.Stops[from].StopId,
                toStopId = corridor.Stops[from + 1].StopId,
                progressPermille = progress,
                reportedDelaySeconds = delaySeconds,
                observedAt = observedAt.ToString("O"),
            }),
        };
        request.Headers.Add(ServiceKeyFilter.Header, ObservationKey);
        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    // The usual start of a test: it is 10:00 in Colombo on the journey's date, the train is on its
    // way from Gampaha to Veyangoda and was seen a moment ago, so Polgahawela is due at 11:00 and
    // Peradeniya at 11:50.
    public async Task<Corridor> RunningCorridorAsync()
    {
        var corridor = await NewCorridorAsync();
        await SetNowAsync(corridor.At("10:00"));
        await ObserveAsync(corridor, "GPH", 800, Clock.Now.AddSeconds(-10));
        return corridor;
    }

    public async Task<Guid> AddPassengerAsync(string subject) =>
        await AddAccountAsync(subject, "Fathima Passenger", roles: Roles.Passenger);

    public Task<HttpResponseMessage> OrderAsync(
        string? subject, string key, Corridor corridor, string stopCode, string collectionPoint, params (string Item, int Quantity)[] items) =>
        SendAsync(subject, HttpMethod.Post, OrderEndpoints.Path, new
        {
            clientRequestKey = key,
            collectionPointId = collectionPoint,
            journeyId = corridor.JourneyId,
            pickupStopId = corridor.Stop(stopCode),
            items = items.Select(i => new { menuItemId = i.Item, quantity = i.Quantity }),
        });

    public Task<HttpResponseMessage> SendAsync(string? subject, HttpMethod method, string path, object? body = null)
    {
        var client = ClientFor(subject is null ? null : Token(subject));
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request);
    }

    // Runs one retry pass, as the background loop would, and returns how many links it tried.
    public async Task<int> RunPlacementPassAsync()
    {
        var tried = 0;
        await WithScopeAsync(async services => tried = await services.GetRequiredService<PlacementProcessor>().RunDueAsync(default));
        return tried;
    }

    public async Task WithScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    public async Task<OrderLink> LinkAsync(Guid id)
    {
        OrderLink? link = null;
        await WithDbAsync(async db => link = await db.OrderLinks.AsNoTracking().SingleAsync(l => l.Id == id));
        return link!;
    }

    public async Task<int> LinkCountAsync(Guid accountId)
    {
        var count = 0;
        await WithDbAsync(async db => count = await db.OrderLinks.CountAsync(l => l.AccountId == accountId));
        return count;
    }

    // What RailBites itself holds for a link ID, or null if it has no such order.
    public async Task<JsonElement?> ProviderOrderAsync(Guid linkId)
    {
        var response = await RailBites.Http.GetAsync($"/v1/order-links/{linkId}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    public async Task<int> ProviderStockAsync(string collectionPoint, string menuItemId)
    {
        var menu = JsonDocument.Parse(await RailBites.Http.GetStringAsync($"/v1/collection-points/{collectionPoint}/menu")).RootElement;
        return menu.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == menuItemId)
            .GetProperty("stockQuantity").GetInt32();
    }
}
