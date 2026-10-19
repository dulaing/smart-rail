using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartRail.Api.Tests.Catering;

public enum ProxyMode
{
    Forward,

    // The request never reaches RailBites; the caller sees a dropped connection.
    DropRequest,

    // RailBites handles the request and commits, but the caller never gets the answer.
    DropResponse,

    // RailBites is reached but answers 503, as a service does while it is overloaded or restarting.
    ServiceUnavailable,
}

// A forwarding proxy that can lose a request or its response, to reproduce a timeout on either
// side of the provider's commit. SmartRail talks to this instead of to RailBites directly.
public sealed class FlakyProxy : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _upstream;

    private FlakyProxy(WebApplication app, string baseUrl, HttpClient upstream)
    {
        _app = app;
        BaseUrl = baseUrl;
        _upstream = upstream;
    }

    public string BaseUrl { get; }

    public ProxyMode Mode { get; set; } = ProxyMode.Forward;

    public Func<HttpRequestMessage, Task>? BeforeResponse { get; set; }

    // Requests that reached RailBites.
    public int Forwarded { get; private set; }

    // The trace identity (W3C traceparent) of every request this proxy forwarded, in order.
    public List<string?> Traceparents { get; } = [];

    public static async Task<FlakyProxy> StartAsync(string upstreamBaseUrl)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var upstream = new HttpClient { BaseAddress = new Uri(upstreamBaseUrl), Timeout = TimeSpan.FromSeconds(15) };

        FlakyProxy? proxy = null;
        app.Run(async context =>
        {
            if (proxy!.Mode == ProxyMode.DropRequest)
            {
                context.Abort();
                return;
            }

            if (proxy.Mode == ProxyMode.ServiceUnavailable)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), context.Request.Path + context.Request.QueryString);
            foreach (var header in context.Request.Headers.Where(h => h.Key.StartsWith("X-RailBites", StringComparison.OrdinalIgnoreCase) || h.Key.Equals("traceparent", StringComparison.OrdinalIgnoreCase)))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            if (context.Request.Method is "POST" or "PUT")
            {
                request.Content = new StreamContent(context.Request.Body);
                if (context.Request.ContentType is { } contentType)
                {
                    request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                }
            }

            proxy.Traceparents.Add(context.Request.Headers["traceparent"].FirstOrDefault());
            HttpResponseMessage response;
            try
            {
                response = await upstream.SendAsync(request);
            }
            catch (HttpRequestException)
            {
                // RailBites is stopped. A real stopped service refuses or drops the connection,
                // never answers 500, and how fast the refusal arrives differs between operating
                // systems. Dropping the connection looks the same to the caller everywhere.
                context.Abort();
                return;
            }

            using var _ = response;
            proxy.Forwarded++;
            if (proxy.BeforeResponse is not null) await proxy.BeforeResponse(request);
            if (proxy.Mode == ProxyMode.DropResponse)
            {
                context.Abort();
                return;
            }

            context.Response.StatusCode = (int)response.StatusCode;
            if (response.Content.Headers.ContentType is { } type)
            {
                context.Response.ContentType = type.ToString();
            }

            await response.Content.CopyToAsync(context.Response.Body);
        });

        await app.StartAsync();
        var address = app.Urls.First();
        proxy = new FlakyProxy(app, address, upstream);
        return proxy;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _upstream.Dispose();
    }
}
