using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SmartRail.Api.Modules.Catering;
using SmartRail.Api.Modules.Catering.Callbacks;

namespace SmartRail.Api.Tests.Catering;

// A real loopback HTTP listener lets the Node process call the in-process SmartRail test host.
public sealed class CallbackApi : CateringApi
{
    public const string InboundKey = "test-railbites-inbound-key";
    public CallbackBridge Bridge { get; private set; } = null!;

    public override async Task InitializeAsync()
    {
        Bridge = await CallbackBridge.StartAsync(() => Client);
        await base.InitializeAsync();
    }

    protected override void ConfigureRailBites(RailBitesProcess railBites)
    {
        railBites.Environment["SMARTRAIL_BASE_URL"] = Bridge.BaseUrl;
        railBites.Environment["SMARTRAIL_SERVICE_KEY"] = InboundKey;
        railBites.Environment["RAILBITES_CALLBACK_SECRET"] = RailBitesProcess.CallbackSecret;
        railBites.Environment["RAILBITES_CALLBACK_POLL_MS"] = "0";
    }

    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        base.ConfigureHost(builder);
        builder.UseSetting(CallbackEndpoints.SecretSetting, RailBitesProcess.CallbackSecret);
        builder.UseSetting(PassageEndpoints.KeySetting, InboundKey);
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (Bridge is not null) await Bridge.DisposeAsync();
    }

    public async Task RunCallbacksAsync()
    {
        var response = await RailBites.Http.PostAsync("/internal/test/callbacks/run", null);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class CallbackBridge : IAsyncDisposable
{
    private readonly WebApplication _app;
    private CallbackBridge(WebApplication app) => _app = app;
    public string BaseUrl => _app.Urls.First();
    public ProxyMode Mode { get; set; }
    public int Forwarded { get; private set; }

    public static async Task<CallbackBridge> StartAsync(Func<HttpClient> target)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var bridge = new CallbackBridge(app);
        app.Run(async context =>
        {
            if (bridge.Mode == ProxyMode.ServiceUnavailable)
            {
                context.Response.StatusCode = 503;
                return;
            }
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), context.Request.Path + context.Request.QueryString);
            foreach (var header in context.Request.Headers.Where(h => h.Key.StartsWith("X-RailBites", StringComparison.OrdinalIgnoreCase)))
                request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            if (context.Request.Method == "POST")
            {
                using var body = new MemoryStream();
                await context.Request.Body.CopyToAsync(body);
                request.Content = new ByteArrayContent(body.ToArray());
                request.Content.Headers.ContentType = new("application/json");
            }
            using var response = await target().SendAsync(request);
            bridge.Forwarded++;
            if (bridge.Mode == ProxyMode.DropResponse)
            {
                context.Abort();
                return;
            }
            context.Response.StatusCode = (int)response.StatusCode;
            await response.Content.CopyToAsync(context.Response.Body);
        });
        await app.StartAsync();
        return bridge;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
