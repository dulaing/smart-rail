using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Access;

// Saving, listing and deleting an account's own reusable origin/destination commutes against a
// disposable PostgreSQL. The test signer stands in for Keycloak.
public class PassengerSavedRoutesApiTests(AccessApi api) : IClassFixture<AccessApi>
{
    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, Guid? origin, Guid? destination) =>
        client.PostAsJsonAsync("/api/me/saved-routes", new { originStationId = origin, destinationStationId = destination });

    [RequiresDatabaseFact]
    public async Task A_guest_is_401_and_a_disabled_account_is_403()
    {
        await api.AddAccountAsync("routes-disabled", status: AccountStatus.Disabled, roles: Roles.Passenger);

        using var guest = api.ClientFor(null);
        using var disabled = api.ClientFor(api.Token("routes-disabled"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/me/saved-routes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await disabled.GetAsync("/api/me/saved-routes")).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task An_account_without_the_permission_is_403_and_audited()
    {
        var id = await api.AddAccountAsync("routes-driver", roles: Roles.Driver);
        using var client = api.ClientFor(api.Token("routes-driver"));
        var origin = await api.StationIdAsync("GPH");
        var destination = await api.StationIdAsync("FOT");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/me/saved-routes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SaveAsync(client, origin, destination)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/me/saved-routes/{Guid.NewGuid()}")).StatusCode);
        Assert.Contains(await api.AuditAsync(id), e => e.Action == Permissions.SavedRoutesManage);
    }

    [RequiresDatabaseFact]
    public async Task A_passenger_saves_lists_and_deletes_their_own_route()
    {
        await api.AddAccountAsync("route-owner", roles: Roles.Passenger);
        using var client = api.ClientFor(api.Token("route-owner"));
        var origin = await api.StationIdAsync("GPH");
        var destination = await api.StationIdAsync("FOT");

        var created = await SaveAsync(client, origin, destination);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var routeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var list = await client.GetFromJsonAsync<JsonElement>("/api/me/saved-routes");
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(origin, list[0].GetProperty("originStationId").GetGuid());
        Assert.Equal(destination, list[0].GetProperty("destinationStationId").GetGuid());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/me/saved-routes/{routeId}")).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/me/saved-routes")).GetArrayLength());
    }

    [RequiresDatabaseFact]
    public async Task A_duplicate_save_returns_the_same_route_without_a_second_row()
    {
        var id = await api.AddAccountAsync("route-dup", roles: Roles.Passenger);
        using var client = api.ClientFor(api.Token("route-dup"));
        var origin = await api.StationIdAsync("GPH");
        var destination = await api.StationIdAsync("FOT");

        var first = await SaveAsync(client, origin, destination);
        var second = await SaveAsync(client, origin, destination);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(firstId, secondId);

        var rows = 0;
        await api.WithDbAsync(async db => rows = await db.SavedRoutes.CountAsync(r => r.AccountId == id));
        Assert.Equal(1, rows);
    }

    [RequiresDatabaseFact]
    public async Task A_save_rejects_unknown_repeated_or_missing_stations()
    {
        await api.AddAccountAsync("route-invalid", roles: Roles.Passenger);
        using var client = api.ClientFor(api.Token("route-invalid"));
        var origin = await api.StationIdAsync("GPH");

        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(client, Guid.NewGuid(), origin)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(client, origin, origin)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(client, null, origin)).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task Another_passengers_route_is_not_visible_or_deletable()
    {
        var owner = await api.AddAccountAsync("route-a", roles: Roles.Passenger);
        await api.AddAccountAsync("route-b", roles: Roles.Passenger);
        var origin = await api.StationIdAsync("GPH");
        var destination = await api.StationIdAsync("FOT");

        Guid routeId;
        using (var a = api.ClientFor(api.Token("route-a")))
        {
            var created = await SaveAsync(a, origin, destination);
            routeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        using var b = api.ClientFor(api.Token("route-b"));
        Assert.Equal(0, (await b.GetFromJsonAsync<JsonElement>("/api/me/saved-routes")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/me/saved-routes/{routeId}")).StatusCode);

        var rows = 0;
        await api.WithDbAsync(async db => rows = await db.SavedRoutes.CountAsync(r => r.AccountId == owner));
        Assert.Equal(1, rows);
    }

    [RequiresDatabaseFact]
    public async Task Saving_a_route_never_creates_a_follow()
    {
        var id = await api.AddAccountAsync("route-not-follow", roles: Roles.Passenger);
        using var client = api.ClientFor(api.Token("route-not-follow"));

        (await SaveAsync(client, await api.StationIdAsync("GPH"), await api.StationIdAsync("FOT"))).EnsureSuccessStatusCode();

        var follows = 0;
        await api.WithDbAsync(async db => follows = await db.FollowedJourneys.CountAsync(f => f.AccountId == id));
        Assert.Equal(0, follows);
    }

    [RequiresDatabaseFact]
    public async Task Repeated_delete_of_a_saved_route_is_404_the_second_time()
    {
        await api.AddAccountAsync("route-repeat-delete", roles: Roles.Passenger);
        using var client = api.ClientFor(api.Token("route-repeat-delete"));

        var created = await SaveAsync(client, await api.StationIdAsync("GPH"), await api.StationIdAsync("FOT"));
        var routeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/me/saved-routes/{routeId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/me/saved-routes/{routeId}")).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task The_database_rejects_a_duplicate_saved_route()
    {
        var id = await api.AddAccountAsync("route-unique", roles: Roles.Passenger);
        var origin = await api.StationIdAsync("GPH");
        var destination = await api.StationIdAsync("FOT");

        await Assert.ThrowsAsync<DbUpdateException>(() => api.WithDbAsync(async db =>
        {
            db.SavedRoutes.Add(new SavedRoute
            {
                Id = Guid.NewGuid(), AccountId = id, OriginStationId = origin, DestinationStationId = destination,
            });
            db.SavedRoutes.Add(new SavedRoute
            {
                Id = Guid.NewGuid(), AccountId = id, OriginStationId = origin, DestinationStationId = destination,
            });
            await db.SaveChangesAsync();
        }));
    }
}
