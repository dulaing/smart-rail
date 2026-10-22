using System.Text;
using System.Text.Json;
using SmartRail.Api.Modules.Catering.Callbacks;

namespace SmartRail.Api.Tests.Catering;

public class CallbackSignatureTests
{
    [Fact]
    public void Both_services_use_the_same_raw_body_signature_and_timestamp_examples()
    {
        using var fixtures = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(GrilledProcess.ServiceFolder(), "fixtures", "signed-callbacks.json")));
        var root = fixtures.RootElement;
        var secret = root.GetProperty("secret").GetString()!;
        var now = root.GetProperty("now").GetDateTimeOffset();
        foreach (var example in root.GetProperty("cases").EnumerateArray())
        {
            var accepted = CallbackSignature.IsValid(secret, example.GetProperty("timestamp").GetInt64().ToString(),
                example.GetProperty("signature").GetString()!, Encoding.UTF8.GetBytes(example.GetProperty("body").GetString()!), now);
            Assert.True(accepted == example.GetProperty("accepted").GetBoolean(), example.GetProperty("name").GetString());
        }
    }

    [Theory]
    [InlineData(null, "1792299600", "v1=")]
    [InlineData("", "1792299600", "v1=")]
    [InlineData("secret", "1792299600,1792299600", "v1=")]
    [InlineData("secret", "9223372036854775807", "v1=")]
    [InlineData("secret", "-1", "v1=")]
    public void Missing_secrets_or_malformed_headers_fail_closed(string? secret, string timestamp, string signature) =>
        Assert.False(CallbackSignature.IsValid(secret, timestamp, signature, [], DateTimeOffset.UtcNow));
}
