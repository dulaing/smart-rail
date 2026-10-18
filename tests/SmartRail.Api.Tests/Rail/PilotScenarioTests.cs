using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Tests.Rail;

// T1 of the pilot acceptance spec: the anchor command reuses the demo seed, so the same anchor
// and run add no journeys, do not reset history, and describe both scenario routes.

public class PilotScenarioAnchorTests
{
    [Theory]
    [InlineData("2026-10-20T08:00:00+05:30", "2026-10-20")]
    [InlineData("2026-10-20T02:30:00Z", "2026-10-20")]      // the same instant, Z form
    [InlineData("2026-10-20T23:30:00-05:00", "2026-10-21")] // the Colombo service date is the next day
    public void An_anchor_maps_to_its_Colombo_service_date(string raw, string expected)
    {
        Assert.True(PilotScenario.TryParseAnchor(raw, out var anchor, out var error), error);
        Assert.Equal(DateOnly.Parse(expected), PilotScenario.ServiceDateOf(anchor));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-10-20T08:00:00")] // no offset
    [InlineData("2026-10-20")]          // a date, not an instant
    [InlineData("tomorrow")]
    public void An_anchor_without_an_offset_is_rejected(string? raw)
    {
        Assert.False(PilotScenario.TryParseAnchor(raw, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public async Task Writing_a_manifest_creates_the_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), "smartrail-pilot-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "scenario.json");
        var manifest = new ScenarioManifest(
            new DateTimeOffset(2026, 10, 17, 6, 0, 0, TimeSpan.FromMinutes(330)),
            "run-1",
            new DateOnly(2026, 10, 17),
            [],
            [],
            []);

        try
        {
            await PilotScenario.WriteManifestAsync(path, manifest);

            Assert.True(File.Exists(path));
            Assert.Contains("\"run\": \"run-1\"", await File.ReadAllTextAsync(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

public class PilotScenarioSeedTests(TimetableApi api) : IClassFixture<TimetableApi>
{
    // 2026-10-17 is a Saturday, so only the daily trains run and the two named routes are unique.
    private static readonly DateTimeOffset Saturday = new(2026, 10, 17, 6, 0, 0, TimeSpan.FromMinutes(330));

    // An anchor outside the fixture's own week, so this test owns the journey counts it checks.
    private static readonly DateTimeOffset FreshAnchor = new(2026, 11, 10, 8, 0, 0, TimeSpan.FromMinutes(330));

    [RequiresDatabaseFact]
    public async Task The_manifest_has_both_directions_and_Polgahawela_before_Peradeniya()
    {
        ScenarioManifest manifest = null!;
        await api.WithDbAsync(async db =>
        {
            manifest = await PilotScenario.BuildManifestAsync(db, Saturday, "run-1");
        });

        var opening = Assert.Single(manifest.Journeys, j => j.Role == PilotScenario.GuestOpeningRole);
        Assert.Equal(PilotScenario.GuestOpeningTrain, opening.TrainNumber);
        Assert.Equal("GPH", opening.Origin);
        Assert.Equal("FOT", opening.Destination);
        Assert.DoesNotContain("PLG", opening.Stops.Select(s => s.StationCode));

        var food = Assert.Single(manifest.Journeys, j => j.Role == PilotScenario.OutboundFoodRole);
        Assert.Equal(PilotScenario.OutboundFoodTrain, food.TrainNumber);
        Assert.Equal("FOT", food.Origin);
        Assert.Equal("PDN", food.Destination);

        var polgahawela = food.Stops.Single(s => s.StationCode == PilotScenario.EarlyEndFrom);
        var peradeniya = food.Stops.Single(s => s.StationCode == PilotScenario.EarlyEndTo);
        Assert.True(polgahawela.Sequence < peradeniya.Sequence);
        Assert.Equal(PilotScenario.EarlyEndFrom, food.EarlyEndStop);
        Assert.Equal(new[] { PilotScenario.EarlyEndTo }, food.UnservedAfterEarlyEnd);

        // The two routes are different services, not one train shown twice.
        Assert.NotEqual(opening.JourneyId, food.JourneyId);
        Assert.NotEqual(opening.TrainNumber, food.TrainNumber);

        Assert.All(manifest.Journeys.SelectMany(j => j.Stops), stop =>
        {
            Assert.NotEqual(Guid.Empty, stop.StopId);
            Assert.NotEqual(Guid.Empty, stop.StationId);
        });
    }

    [RequiresDatabaseFact]
    public async Task Repeating_the_same_anchor_adds_no_journeys_and_does_not_reset_history()
    {
        var serviceDate = PilotScenario.ServiceDateOf(FreshAnchor);
        ScenarioManifest first = null!;
        var journeysAfterFirstSeed = 0;

        await api.WithDbAsync(async db =>
        {
            await DatabaseSetup.SeedAsync(db, serviceDate, 3);
            first = await PilotScenario.BuildManifestAsync(db, FreshAnchor, "run-1");
            journeysAfterFirstSeed = await db.Journeys.CountAsync();

            // Stand in for a published change: the version has moved off zero.
            var version = await db.JourneyVersions.SingleAsync(v => v.JourneyId == first.Journeys[0].JourneyId);
            version.Version = 5;
            await db.SaveChangesAsync();
        });

        ScenarioManifest second = null!;
        await api.WithDbAsync(async db =>
        {
            await DatabaseSetup.SeedAsync(db, serviceDate, 3);
            second = await PilotScenario.BuildManifestAsync(db, FreshAnchor, "run-1");

            Assert.Equal(journeysAfterFirstSeed, await db.Journeys.CountAsync());
            Assert.Equal(5, await db.JourneyVersions
                .Where(v => v.JourneyId == first.Journeys[0].JourneyId)
                .Select(v => v.Version)
                .SingleAsync());
        });

        Assert.Equal(first.Journeys.Select(j => j.JourneyId), second.Journeys.Select(j => j.JourneyId));
    }

    [RequiresDatabaseFact]
    public async Task The_manifest_lists_no_credentials()
    {
        ScenarioManifest manifest = null!;
        await api.WithDbAsync(async db =>
        {
            manifest = await PilotScenario.BuildManifestAsync(db, Saturday, "run-1");
        });

        var json = PilotScenario.ToJson(manifest).ToLowerInvariant();
        Assert.DoesNotContain("password", json);
        Assert.DoesNotContain("secret", json);
        Assert.DoesNotContain("token", json);
    }
}
