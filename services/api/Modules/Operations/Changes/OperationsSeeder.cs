using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Operations.Changes;

public static class OperationsSeeder
{
    // Gives every journey a version row at 0. Running it again adds only the missing rows and
    // never resets a version that has moved on.
    public static async Task SeedVersionsAsync(
        SmartRailDbContext db, IJourneyStopReader journeys, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var all = await journeys.AllJourneyIdsAsync(cancellationToken);
        var existing = await db.JourneyVersions.Select(v => v.JourneyId).ToListAsync(cancellationToken);

        var missing = all.Except(existing).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        db.JourneyVersions.AddRange(missing.Select(id => new JourneyChangeVersion { JourneyId = id, Version = 0, UpdatedAt = now }));
        await db.SaveChangesAsync(cancellationToken);
    }
}
