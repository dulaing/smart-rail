using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Net.Http.Headers;

namespace SmartRail.Api.Infrastructure;

public static class ETagResults
{
    /// <summary>
    /// Returns the body as JSON with an ETag made from its bytes, or 304 when the client's
    /// <c>If-None-Match</c> already has this exact content. A changed body gives a new ETag.
    /// </summary>
    public static IResult JsonWithETag<T>(HttpContext context, T body)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(body, JsonSerializerOptions.Web);
        var etag = new EntityTagHeaderValue($"\"{Convert.ToHexString(SHA256.HashData(json))[..32]}\"");

        context.Response.Headers.ETag = etag.ToString();
        // Clients may cache but must revalidate, so a changed timetable shows up on the next poll.
        context.Response.Headers.CacheControl = "no-cache";

        var ifNoneMatch = context.Request.GetTypedHeaders().IfNoneMatch;
        if (ifNoneMatch.Any(candidate => candidate.Compare(etag, useStrongComparison: false)
            || candidate == EntityTagHeaderValue.Any))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Bytes(json, "application/json");
    }
}
