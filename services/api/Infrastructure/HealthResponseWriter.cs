using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartRail.Api.Infrastructure;

// The JSON body of /health/ready: the overall status and, for each check, its status, a plain
// description and the numbers behind it. Checks put only counts and ages in their data; this writer
// never adds an exception message or anything else from inside a check.
public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                // A check that threw carries the exception's text as its description; that can name
                // tables or hosts, so only a fixed sentence is shown for it.
                description = entry.Value.Exception is null ? entry.Value.Description : "The check could not complete.",
                data = entry.Value.Data,
            }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, Options));
    }
}
