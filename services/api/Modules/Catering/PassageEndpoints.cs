using System.Security.Cryptography;
using System.Text;
using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Catering;

public static class PassageEndpoints
{
    public const string KeySetting = "Grilled:InboundServiceKey";
    public const string KeyHeader = "X-Grilled-Service-Key";

    public static void MapPickupPassage(this IEndpointRouteBuilder app) =>
        app.MapGet("/internal/catering/journeys/{journeyId:guid}/stops/{stopId:guid}/passage", Read).AllowAnonymous();

    private static async Task<IResult> Read(Guid journeyId, Guid stopId, HttpContext context,
        IConfiguration configuration, IPickupReader pickups, TimeProvider time)
    {
        context.Response.Headers.CacheControl = "no-store";
        var configured = configuration[KeySetting];
        var presented = context.Request.Headers[KeyHeader].ToString();
        if (string.IsNullOrEmpty(configured) || string.IsNullOrEmpty(presented)
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(configured)), SHA256.HashData(Encoding.UTF8.GetBytes(presented))))
        {
            return Results.Json(new { code = "SERVICE_KEY_REQUIRED", message = "Service key required." }, statusCode: 401);
        }

        var state = await pickups.ReadPassageAsync(journeyId, stopId, context.RequestAborted);
        return state is null
            ? Results.Json(new { code = "STOP_NOT_FOUND", message = "Unknown journey stop." }, statusCode: 404)
            : Results.Ok(new { state = state.Value.ToString().ToUpperInvariant(), evaluatedAt = time.GetUtcNow() });
    }
}
