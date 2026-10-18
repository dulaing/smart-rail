using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Tests.Catering;

public class HealthResponseWriterTests
{
    // Found by running the real stack: before the catering tables existed, the readiness body printed
    // the database's own error text. A check that threw must never put that text on a public route.
    [Fact]
    public async Task A_check_that_threw_shows_a_fixed_sentence_not_the_exception_text()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["catering"] = new(HealthStatus.Degraded, "42P01: relation \"catering.outbox_messages\" does not exist", TimeSpan.Zero,
                    new InvalidOperationException("relation \"catering.outbox_messages\" does not exist"), new Dictionary<string, object>()),
                ["fine"] = new(HealthStatus.Healthy, "All good.", TimeSpan.Zero, null, new Dictionary<string, object> { ["count"] = 3 }),
            },
            TimeSpan.Zero);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await HealthResponseWriter.WriteAsync(context, report);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("outbox_messages", body);
        Assert.Contains("The check could not complete.", body);
        Assert.Contains("All good.", body);
        Assert.Contains("\"count\":3", body);
    }
}
