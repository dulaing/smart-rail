using System.Security.Cryptography;
using System.Text;

namespace SmartRail.Api.Modules.Rail.Running;

public static class ServiceKeys
{
    // Fails closed: with no key configured nothing matches, so a missing setting can never
    // open a write route. Hashing first makes the comparison independent of the key's length.
    public static bool Matches(string? configured, string? presented)
    {
        if (string.IsNullOrEmpty(configured) || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(configured)),
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)));
    }
}

// Protects the internal write routes with a shared key from configuration (user-secrets or the
// environment), not a user account. An anonymous or wrongly keyed call never reaches the handler.
public sealed class ServiceKeyFilter(IConfiguration configuration, ILogger<ServiceKeyFilter> logger) : IEndpointFilter
{
    public const string Header = "X-SmartRail-Service-Key";
    public const string ConfigurationKey = "Observations:ServiceKey";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configured = configuration[ConfigurationKey];
        if (string.IsNullOrEmpty(configured))
        {
            logger.LogWarning("Rejected an internal call because {Setting} is not configured.", ConfigurationKey);
        }

        if (ServiceKeys.Matches(configured, context.HttpContext.Request.Headers[Header].ToString()))
        {
            return next(context);
        }

        return ValueTask.FromResult<object?>(Results.Problem(
            title: "Service key required",
            detail: $"Send the configured service key in the {Header} header.",
            statusCode: StatusCodes.Status401Unauthorized));
    }
}
