using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Operations.Changes;

// A journey's change version with the history that version covers.
public sealed record JourneyChanges(int Version, IReadOnlyList<ServiceChange> History);

// Reads Operations' own tables. Journeys and stops come from Rail through its reader.
public sealed class ChangeStore(SmartRailDbContext db)
{
    // The version first, then only history up to it. Rows are committed together with the
    // version bump, so the two always belong to the same moment. Null when the journey has no
    // version row.
    public async Task<JourneyChanges?> LoadAsync(Guid journeyId, CancellationToken cancellationToken)
    {
        var version = await db.JourneyVersions.AsNoTracking()
            .Where(v => v.JourneyId == journeyId)
            .Select(v => (int?)v.Version)
            .SingleOrDefaultAsync(cancellationToken);
        if (version is null)
        {
            return null;
        }

        var history = await db.ServiceChanges.AsNoTracking()
            .Where(c => c.JourneyId == journeyId && c.JourneyVersion <= version)
            .OrderBy(c => c.JourneyVersion)
            .ToListAsync(cancellationToken);
        return new JourneyChanges(version.Value, history);
    }
}

public static class OperationsJourneys
{
    // The journey as the change rules see it, or null for an unknown journey.
    public static async Task<OperationsJourney?> FindOperationsJourneyAsync(
        this IJourneyStopReader reader, Guid journeyId, CancellationToken cancellationToken)
    {
        var journey = await reader.FindAsync(journeyId, cancellationToken);
        return journey is null
            ? null
            : new OperationsJourney(
                journey.JourneyId,
                journey.Stops.Select(s => new JourneyStop(
                    s.StopId, s.StationId, s.StationName, s.Sequence, s.HasScheduledArrival, s.PlannedPlatform)));
    }
}
