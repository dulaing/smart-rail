using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SmartRail.Api.Modules.Catering.Callbacks;

public enum SignatureProblem
{
    None,
    SecretNotConfigured,
    MissingHeaders,
    BadTimestamp,
    TimestampOutOfRange,
    BadSignature,
}

// The signature on a RailBites callback: HMAC-SHA256, keyed with the shared secret, over the UTF-8
// text "<timestamp>." followed by the exact bytes of the request body, written as "v1=<lower hex>".
// The check is made on the raw bytes before anything is parsed, so reformatting a body breaks it.
public static class CallbackSignature
{
    public const string SignatureHeader = "X-RailBites-Signature";
    public const string TimestampHeader = "X-RailBites-Timestamp";
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);
    private const string Version = "v1=";

    public static string Sign(string secret, long timestampSeconds, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes($"{timestampSeconds.ToString(CultureInfo.InvariantCulture)}.");
        var data = new byte[prefix.Length + body.Length];
        prefix.CopyTo(data, 0);
        body.CopyTo(data.AsSpan(prefix.Length));
        return Version + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), data));
    }

    public static bool IsValid(string? secret, string? timestamp, string? signature, ReadOnlySpan<byte> body, DateTimeOffset now) =>
        Check(secret, timestamp, signature, body, now) == SignatureProblem.None;

    // Fails closed: with no secret configured nothing is ever accepted.
    public static SignatureProblem Check(
        string? secret, string? timestamp, string? signature, ReadOnlySpan<byte> body, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return SignatureProblem.SecretNotConfigured;
        }

        if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signature))
        {
            return SignatureProblem.MissingHeaders;
        }

        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return SignatureProblem.BadTimestamp;
        }

        // DateTimeOffset cannot represent a timestamp outside years 1 to 9999.
        if (seconds is < -62135596800L or > 253402300799L)
        {
            return SignatureProblem.BadTimestamp;
        }

        var difference = now - DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (difference.Duration() > Tolerance)
        {
            return SignatureProblem.TimestampOutOfRange;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(secret, seconds, body));
        var provided = Encoding.ASCII.GetBytes(signature);
        return expected.Length == provided.Length && CryptographicOperations.FixedTimeEquals(expected, provided)
            ? SignatureProblem.None
            : SignatureProblem.BadSignature;
    }
}
