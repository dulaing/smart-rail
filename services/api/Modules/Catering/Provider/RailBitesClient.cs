using System.Net.Http.Json;
using System.Text.Json;

namespace SmartRail.Api.Modules.Catering.Provider;

// SmartRail's side of the RailBites HTTP contract. Every method answers with a ProviderResponse
// instead of throwing, so a caller has to decide what an unknown outcome means.
public interface IRailBitesClient
{
    Task<ProviderResponse<ProviderCollectionPoints>> ListCollectionPointsAsync(CancellationToken cancellationToken);

    Task<ProviderResponse<ProviderMenu>> GetMenuAsync(string collectionPointId, CancellationToken cancellationToken);

    Task<ProviderResponse<ProviderOrder>> PlaceAsync(Guid orderLinkId, PlaceOrderBody body, CancellationToken cancellationToken);

    Task<ProviderResponse<ProviderOrder>> LookupAsync(Guid orderLinkId, CancellationToken cancellationToken);

    Task<ProviderResponse<ProviderOrder>> CancelAsync(Guid orderLinkId, CancelOrderBody body, CancellationToken cancellationToken);

    // Sends one stored train-change message exactly as it was written. Success means RailBites has
    // committed it (as new or as a repeat); anything else leaves it to be sent again.
    Task<ProviderResponse<EventReceipt>> DeliverEventAsync(string payloadJson, CancellationToken cancellationToken);

    // Whether RailBites says it is ready (its /health/ready, which needs no credential).
    Task<ProviderReadiness> CheckReadyAsync(CancellationToken cancellationToken);

    // The Development-only outage switch. Its own route stays reachable while the provider is
    // "down", so a stale outage can always be ended in one call.
    Task<ProviderResponse<ProviderOutage>> GetOutageAsync(CancellationToken cancellationToken);

    Task<ProviderResponse<ProviderOutage>> SetOutageAsync(bool active, CancellationToken cancellationToken);
}

public enum ProviderReadiness
{
    Ready,
    NotReady,
    Unreachable,
    NotConfigured,
}

public sealed class RailBitesClient(HttpClient http, IConfiguration configuration, ILogger<RailBitesClient> logger) : IRailBitesClient
{
    public const string ServiceKeyHeader = "X-RailBites-Service-Key";

    public Task<ProviderResponse<ProviderCollectionPoints>> ListCollectionPointsAsync(CancellationToken cancellationToken) =>
        SendAsync<ProviderCollectionPoints>(HttpMethod.Get, "/v1/collection-points", null, cancellationToken);

    public Task<ProviderResponse<ProviderMenu>> GetMenuAsync(string collectionPointId, CancellationToken cancellationToken) =>
        SendAsync<ProviderMenu>(HttpMethod.Get, $"/v1/collection-points/{Uri.EscapeDataString(collectionPointId)}/menu", null, cancellationToken);

    public Task<ProviderResponse<ProviderOrder>> PlaceAsync(Guid orderLinkId, PlaceOrderBody body, CancellationToken cancellationToken) =>
        SendAsync<ProviderOrder>(HttpMethod.Put, $"/v1/order-links/{orderLinkId}", body, cancellationToken);

    public Task<ProviderResponse<ProviderOrder>> LookupAsync(Guid orderLinkId, CancellationToken cancellationToken) =>
        SendAsync<ProviderOrder>(HttpMethod.Get, $"/v1/order-links/{orderLinkId}", null, cancellationToken);

    public Task<ProviderResponse<ProviderOrder>> CancelAsync(Guid orderLinkId, CancelOrderBody body, CancellationToken cancellationToken) =>
        SendAsync<ProviderOrder>(HttpMethod.Post, $"/v1/order-links/{orderLinkId}/cancellation", body, cancellationToken);

    public Task<ProviderResponse<EventReceipt>> DeliverEventAsync(string payloadJson, CancellationToken cancellationToken) =>
        SendAsync<EventReceipt>(HttpMethod.Post, "/v1/journey-events", payloadJson, cancellationToken);

    public Task<ProviderResponse<ProviderOutage>> GetOutageAsync(CancellationToken cancellationToken) =>
        SendAsync<ProviderOutage>(HttpMethod.Get, "/internal/dev/outage", null, cancellationToken);

    public Task<ProviderResponse<ProviderOutage>> SetOutageAsync(bool active, CancellationToken cancellationToken) =>
        SendAsync<ProviderOutage>(HttpMethod.Put, "/internal/dev/outage", new { active }, cancellationToken);

    public async Task<ProviderReadiness> CheckReadyAsync(CancellationToken cancellationToken)
    {
        var baseUrl = configuration["RailBites:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return ProviderReadiness.NotConfigured;
        }

        // A health probe must not hang: it gives up well before the order timeout.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var response = await http.GetAsync(new Uri(new Uri(baseUrl), "/health/ready"), limit.Token);
            return response.IsSuccessStatusCode ? ProviderReadiness.Ready : ProviderReadiness.NotReady;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return ProviderReadiness.Unreachable;
        }
    }

    private async Task<ProviderResponse<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var baseUrl = configuration["RailBites:BaseUrl"];
        var key = configuration["RailBites:ServiceKey"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(key))
        {
            // Fail closed: without both settings nothing is sent.
            logger.LogWarning("RailBites is not configured (RailBites:BaseUrl and RailBites:ServiceKey).");
            return ProviderResponse<T>.Unknown("PROVIDER_NOT_CONFIGURED");
        }

        using var request = new HttpRequestMessage(method, new Uri(new Uri(baseUrl), path));
        request.Headers.Add(ServiceKeyHeader, key);
        if (body is not null)
        {
            // A string is a body that was already written and must not be reformatted.
            request.Content = body is string json
                ? new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                : JsonContent.Create(body, body.GetType(), options: ProviderJson.Options);
        }

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            var status = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(ProviderJson.Options, cancellationToken);
                return value is null
                    ? ProviderResponse<T>.Unknown("PROVIDER_BAD_RESPONSE", status)
                    : ProviderResponse<T>.Success(value, status);
            }

            var error = await ReadErrorAsync(response, cancellationToken);
            if (status == StatusCodes.Status404NotFound && error.Code == "ORDER_NOT_FOUND")
            {
                return ProviderResponse<T>.NotFound(error);
            }

            // 5xx, and 401 (a wrong key is our configuration fault, not a verdict on the order),
            // leave the outcome unknown. Any other 4xx is the provider's considered answer.
            if (status >= 500 || status == StatusCodes.Status401Unauthorized)
            {
                logger.LogWarning("RailBites answered {Status} {Code} for {Method} {Path}.", status, error.Code, method, path);
                return ProviderResponse<T>.Unknown(error.Code, status);
            }

            return ProviderResponse<T>.Refused(error, status);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogWarning("RailBites could not be reached for {Method} {Path}: {Reason}.", method, path, exception.GetType().Name);
            return ProviderResponse<T>.Unknown("PROVIDER_UNAVAILABLE");
        }
    }

    private static async Task<ProviderError> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ProviderError>(ProviderJson.Options, cancellationToken);
            if (error is not null && !string.IsNullOrEmpty(error.Code))
            {
                return error;
            }
        }
        catch (JsonException)
        {
        }

        return new ProviderError($"HTTP_{(int)response.StatusCode}", response.ReasonPhrase ?? "Error");
    }
}
