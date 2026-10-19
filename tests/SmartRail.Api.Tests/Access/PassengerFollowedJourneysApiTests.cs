using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Access;

// Following and unfollowing one dated journey as its owner, against a disposable PostgreSQL.
public class PassengerFollowedJourneysApiTests(AccessApi api) : IClassFixture<AccessApi>
{
    [RequiresDatabaseFact]
    public async Task A_guest_is_401_and_a_follow_needs_a_permission()
    {
        var id = await api.AddAccountAsync("follow-driver", roles: Roles.Driver);
        var journey = await api.JourneyIdAsync();

        using var guest = api.ClientFor(null);
        using var driver = api.ClientFor(api.Token("follow-driver"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/me/followed-journeys")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.GetAsync("/api/me/followed-journeys")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.PutAsync($"/api/me/followed-journeys/{journey}", null)).StatusCode);
        Assert.Contains(await api.AuditAsync(id), e => e.Action == Permissions.FollowsManage);
    }

    [RequiresDatabaseFact]
    public async Task A_passenger_follows_and_unfollows_a_dated_journey()
    {
        await api.AddAccountAsync("follow-owner", roles: Roles.Passenger);
        using var client = api.ClientFor(api.Token("follow-owner"));
        var journey = await api.JourneyIdAsync();

        var first = await client.PutAsync($"/api/me/followed-journeys/{journey}", null);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var followedAt = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("followedAt").GetDateTimeOffset();
        Assert.NotEqual(default, followedAt);

        // Following again keeps the original time: it is not a second row or a new follow.
        // PostgreSQL stores microseconds, so compare to that precision.
        var second = await client.PutAsync($"/api/me/followed-journeys/{journey}", null);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var again = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("followedAt").GetDateTimeOffset();
        Assert.True((again - followedAt).Duration() < TimeSpan.FromMicroseconds(1));

        var list = await client.GetFromJsonAsync<JsonElement>("/api/me/followed-journeys");
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(journey, list[0].GetProperty("journeyId").GetGuid());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/me/followed-journeys/{journey}")).StatusCode);
        // Unfollowing again is still a success, so a retry cannot fail.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/me/followed-journeys/{journey}")).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/me/followed-journeys")).GetArrayLength());
    }

    [RequiresDatabaseFact]
    public async Task Following_an_unknown_journey_is_400()
    {
        await api.AddAccountAsync("follow-invalid", roles: Roles.Passenger);
        using var client = api.ClientFor(api.Token("follow-invalid"));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync($"/api/me/followed-journeys/{Guid.NewGuid()}", null)).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task A_follow_targets_one_dated_journey_not_every_train_with_its_number()
    {
        await api.AddAccountAsync("follow-dated", roles: Roles.Passenger);
        using var client = api.ClientFor(api.Token("follow-dated"));

        // The same pattern runs on several dates, so it has several dated journeys.
        var journeys = new List<Guid>();
        await api.WithDbAsync(async db => journeys = await db.Journeys
            .Where(j => j.Pattern.TrainNumber == "SIM-1003")
            .OrderBy(j => j.ServiceDate).Select(j => j.Id).Take(2).ToListAsync());
        Assert.Equal(2, journeys.Count);

        (await client.PutAsync($"/api/me/followed-journeys/{journeys[0]}", null)).EnsureSuccessStatusCode();

        var list = await client.GetFromJsonAsync<JsonElement>("/api/me/followed-journeys");
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(journeys[0], list[0].GetProperty("journeyId").GetGuid());
    }

    [RequiresDatabaseFact]
    public async Task Another_passengers_follow_is_not_visible_and_unfollow_does_not_remove_it()
    {
        var owner = await api.AddAccountAsync("follow-a", roles: Roles.Passenger);
        await api.AddAccountAsync("follow-b", roles: Roles.Passenger);
        var journey = await api.JourneyIdAsync();

        using (var a = api.ClientFor(api.Token("follow-a")))
        {
            (await a.PutAsync($"/api/me/followed-journeys/{journey}", null)).EnsureSuccessStatusCode();
        }

        using var b = api.ClientFor(api.Token("follow-b"));
        Assert.Equal(0, (await b.GetFromJsonAsync<JsonElement>("/api/me/followed-journeys")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await b.DeleteAsync($"/api/me/followed-journeys/{journey}")).StatusCode);

        var rows = 0;
        await api.WithDbAsync(async db => rows = await db.FollowedJourneys.CountAsync(f => f.AccountId == owner));
        Assert.Equal(1, rows);
    }

    [RequiresDatabaseFact]
    public async Task The_database_rejects_a_duplicate_follow()
    {
        var id = await api.AddAccountAsync("follow-unique", roles: Roles.Passenger);
        var journey = await api.JourneyIdAsync();
        var now = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<DbUpdateException>(() => api.WithDbAsync(async db =>
        {
            db.FollowedJourneys.Add(new FollowedJourney { Id = Guid.NewGuid(), AccountId = id, JourneyId = journey, FollowedAt = now });
            db.FollowedJourneys.Add(new FollowedJourney { Id = Guid.NewGuid(), AccountId = id, JourneyId = journey, FollowedAt = now });
            await db.SaveChangesAsync();
        }));
    }
}
