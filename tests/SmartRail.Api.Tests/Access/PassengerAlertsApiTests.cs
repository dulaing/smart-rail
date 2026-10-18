using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Access;

// Personal changes for followed dated journeys: the current projection of Operations' history,
// with no alert/store row of its own, against a disposable PostgreSQL.
public class PassengerAlertsApiTests(AccessApi api) : IClassFixture<AccessApi>
{
    private async Task FollowAsync(Guid journeyId, string subject)
    {
        using var client = api.ClientFor(api.Token(subject));
        var response = await client.PutAsync($"/api/me/followed-journeys/{journeyId}", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<List<Guid>> StopIdsAsync(Guid journeyId)
    {
        var stops = new List<Guid>();
        await api.WithDbAsync(async db => stops = await db.Journeys.Where(j => j.Id == journeyId)
            .SelectMany(j => j.Pattern.Stops).OrderBy(s => s.Sequence).Select(s => s.Id).ToListAsync());
        return stops;
    }

    // Stores a history row and moves the journey to that version, as a publish or withdrawal will.
    private async Task<ServiceChange> AddChangeAsync(
        Guid journeyId, int version, Guid publisher, ChangeDetails details,
        Guid? supersedes = null, ChangeAction action = ChangeAction.Publish,
        Guid? id = null, DateTimeOffset? at = null)
    {
        var change = new ServiceChange(id ?? Guid.NewGuid(), version, action, details, supersedes)
        {
            JourneyId = journeyId,
            PublishedById = publisher,
            PublishedAt = at ?? DateTimeOffset.UtcNow,
        };
        await api.WithDbAsync(async db =>
        {
            db.ServiceChanges.Add(change);
            (await db.JourneyVersions.SingleAsync(v => v.JourneyId == journeyId)).Version = version;
            await db.SaveChangesAsync();
        });
        return change;
    }

    private async Task<(HttpResponseMessage Response, JsonElement Body)> AlertsAsync(string subject, string? etag = null)
    {
        using var client = api.ClientFor(api.Token(subject));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me/alerts");
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement);
    }

    [RequiresDatabaseFact]
    public async Task A_followed_journey_with_no_changes_has_no_alerts()
    {
        await api.AddAccountAsync("alerts-plain", roles: Roles.Passenger);
        await FollowAsync(await api.JourneyIdAsync(), "alerts-plain");

        var (response, body) = await AlertsAsync("alerts-plain");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.GetArrayLength());
    }

    [RequiresDatabaseFact]
    public async Task Joining_a_follow_shows_changes_confirmed_before_it()
    {
        await api.AddAccountAsync("alerts-late", roles: Roles.Passenger);
        var journey = await api.JourneyIdAsync(1);
        var publisher = await api.AddAccountAsync("alerts-publisher", roles: Roles.Controller);
        await AddChangeAsync(journey, 1, publisher, ChangeDetails.Delay(10));

        await FollowAsync(journey, "alerts-late");

        var (_, body) = await AlertsAsync("alerts-late");
        Assert.Equal(1, body.GetArrayLength());
        Assert.Equal("DELAY", body[0].GetProperty("kind").GetString());
        Assert.Equal(1, body[0].GetProperty("journeyVersion").GetInt32());
    }

    [RequiresDatabaseFact]
    public async Task A_replacement_shows_only_the_change_now_in_force()
    {
        await api.AddAccountAsync("alerts-replace", roles: Roles.Passenger);
        var journey = await api.JourneyIdAsync(2);
        var publisher = await api.AddAccountAsync("alerts-publisher-2", roles: Roles.Controller);
        await FollowAsync(journey, "alerts-replace");

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await AddChangeAsync(journey, 1, publisher, ChangeDetails.Delay(10), id: first);
        await AddChangeAsync(journey, 2, publisher, ChangeDetails.Delay(30), id: second);

        var (_, body) = await AlertsAsync("alerts-replace");
        Assert.Single(body.EnumerateArray());
        Assert.Equal(second, body[0].GetProperty("changeId").GetGuid());
        Assert.Equal(2, body[0].GetProperty("journeyVersion").GetInt32());
        Assert.DoesNotContain(first.ToString(), body.GetRawText());
    }

    [RequiresDatabaseFact]
    public async Task A_withdrawal_removes_the_item()
    {
        await api.AddAccountAsync("alerts-withdraw", roles: Roles.Passenger);
        var journey = await api.JourneyIdAsync(3);
        var publisher = await api.AddAccountAsync("alerts-publisher-3", roles: Roles.Controller);
        await FollowAsync(journey, "alerts-withdraw");

        var cancel = await AddChangeAsync(journey, 1, publisher, ChangeDetails.Cancel());
        Assert.Single((await AlertsAsync("alerts-withdraw")).Body.EnumerateArray());

        await AddChangeAsync(journey, 2, publisher, new ChangeDetails(ChangeKind.Cancel),
            supersedes: cancel.Id, action: ChangeAction.Withdraw);

        Assert.Equal(0, (await AlertsAsync("alerts-withdraw")).Body.GetArrayLength());
    }

    [RequiresDatabaseFact]
    public async Task An_early_ending_affects_the_final_stop_and_everything_after_it()
    {
        await api.AddAccountAsync("alerts-early", roles: Roles.Passenger);
        var journey = await api.JourneyIdAsync(4);
        var stops = await StopIdsAsync(journey);
        var publisher = await api.AddAccountAsync("alerts-publisher-4", roles: Roles.Controller);
        await FollowAsync(journey, "alerts-early");
        await AddChangeAsync(journey, 1, publisher, ChangeDetails.EarlyEnd(stops[1]));

        var (_, body) = await AlertsAsync("alerts-early");
        Assert.Single(body.EnumerateArray());
        Assert.Equal("EARLY_END", body[0].GetProperty("kind").GetString());
        Assert.Equal(stops.Skip(1), body[0].GetProperty("affectedStopIds").EnumerateArray().Select(s => s.GetGuid()));
    }

    [RequiresDatabaseFact]
    public async Task A_platform_change_affects_only_its_stop_and_a_delay_the_whole_journey()
    {
        await api.AddAccountAsync("alerts-stops", roles: Roles.Passenger);
        var journey = await api.JourneyIdAsync(5);
        var stops = await StopIdsAsync(journey);
        var publisher = await api.AddAccountAsync("alerts-publisher-5", roles: Roles.Controller);
        await FollowAsync(journey, "alerts-stops");

        await AddChangeAsync(journey, 1, publisher, ChangeDetails.Platform(stops[0], "9"));
        await AddChangeAsync(journey, 2, publisher, ChangeDetails.Delay(5));

        var (_, body) = await AlertsAsync("alerts-stops");
        Assert.Equal(2, body.GetArrayLength());
        var byKind = body.EnumerateArray().ToDictionary(a => a.GetProperty("kind").GetString()!);
        Assert.Equal([stops[0]], byKind["PLATFORM"].GetProperty("affectedStopIds").EnumerateArray().Select(s => s.GetGuid()));
        Assert.Equal(stops, byKind["DELAY"].GetProperty("affectedStopIds").EnumerateArray().Select(s => s.GetGuid()));
    }

    [RequiresDatabaseFact]
    public async Task Alerts_are_sorted_by_confirmation_time()
    {
        await api.AddAccountAsync("alerts-order", roles: Roles.Passenger);
        var journey = await api.JourneyIdAsync(6);
        var stops = await StopIdsAsync(journey);
        var publisher = await api.AddAccountAsync("alerts-publisher-6", roles: Roles.Controller);
        await FollowAsync(journey, "alerts-order");

        var start = new DateTimeOffset(2026, 10, 18, 6, 0, 0, TimeSpan.Zero);
        var later = await AddChangeAsync(journey, 1, publisher, ChangeDetails.Platform(stops[0], "9"), at: start.AddMinutes(5));
        var earlier = await AddChangeAsync(journey, 2, publisher, ChangeDetails.Delay(5), at: start);

        var (_, body) = await AlertsAsync("alerts-order");
        Assert.Equal([earlier.Id, later.Id], body.EnumerateArray().Select(a => a.GetProperty("changeId").GetGuid()));
    }

    [RequiresDatabaseFact]
    public async Task A_repeated_poll_is_unchanged_and_returns_304_with_its_etag()
    {
        await api.AddAccountAsync("alerts-etag", roles: Roles.Passenger);
        var journey = await api.JourneyIdAsync(7);
        var publisher = await api.AddAccountAsync("alerts-publisher-7", roles: Roles.Controller);
        await FollowAsync(journey, "alerts-etag");
        await AddChangeAsync(journey, 1, publisher, ChangeDetails.Delay(10));

        var first = await AlertsAsync("alerts-etag");
        var etag = first.Response.Headers.ETag!.Tag;

        Assert.Equal(HttpStatusCode.NotModified, (await AlertsAsync("alerts-etag", etag)).Response.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task Another_passengers_follow_produces_no_alerts_and_unfollowing_stops_them()
    {
        await api.AddAccountAsync("alerts-a", roles: Roles.Passenger);
        await api.AddAccountAsync("alerts-b", roles: Roles.Passenger);
        var journey = await api.JourneyIdAsync(8);
        var publisher = await api.AddAccountAsync("alerts-publisher-8", roles: Roles.Controller);
        await FollowAsync(journey, "alerts-a");
        await AddChangeAsync(journey, 1, publisher, ChangeDetails.Delay(10));

        Assert.Equal(1, (await AlertsAsync("alerts-a")).Body.GetArrayLength());
        Assert.Equal(0, (await AlertsAsync("alerts-b")).Body.GetArrayLength());

        using var client = api.ClientFor(api.Token("alerts-a"));
        (await client.DeleteAsync($"/api/me/followed-journeys/{journey}")).EnsureSuccessStatusCode();
        Assert.Equal(0, (await AlertsAsync("alerts-a")).Body.GetArrayLength());
    }

    [RequiresDatabaseFact]
    public async Task Alerts_never_name_the_confirmer()
    {
        await api.AddAccountAsync("alerts-secret", roles: Roles.Passenger);
        var journey = await api.JourneyIdAsync(9);
        var publisher = await api.AddAccountAsync("alerts-secret-publisher", "Priya Nandadasa", roles: Roles.Controller);
        await FollowAsync(journey, "alerts-secret");
        await AddChangeAsync(journey, 1, publisher, ChangeDetails.Delay(10));

        var text = (await AlertsAsync("alerts-secret")).Body.GetRawText();

        Assert.DoesNotContain("Priya Nandadasa", text);
        Assert.DoesNotContain(publisher.ToString(), text);
        Assert.DoesNotContain("publishedBy", text, StringComparison.OrdinalIgnoreCase);
    }

    [RequiresDatabaseFact]
    public async Task An_account_without_the_alerts_permission_is_403()
    {
        await api.AddAccountAsync("alerts-driver", roles: Roles.Driver);

        var (response, _) = await AlertsAsync("alerts-driver");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
