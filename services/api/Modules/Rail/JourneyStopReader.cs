using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Rail;

// One stop occurrence of a journey, as other modules may see it: no EF entity leaves Rail.
public sealed record RailStop(
    Guid StopId,
    Guid StationId,
    string StationName,
    int Sequence,
    bool HasScheduledArrival,
    string? PlannedPlatform);

public sealed record RailJourney(Guid JourneyId, IReadOnlyList<RailStop> Stops);

// The small door other modules use to read journeys and their stops. Operations goes through
// this instead of querying Rail's tables.
public interface IJourneyStopReader
{
    // The journey and its stop occurrences in running order, or null for an unknown journey.
    Task<RailJourney?> FindAsync(Guid journeyId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> AllJourneyIdsAsync(CancellationToken cancellationToken);
}

public sealed class JourneyStopReader(SmartRailDbContext db) : IJourneyStopReader
{
    public async Task<RailJourney?> FindAsync(Guid journeyId, CancellationToken cancellationToken)
    {
        var patternId = await db.Journeys.AsNoTracking()
            .Where(j => j.Id == journeyId)
            .Select(j => (Guid?)j.PatternId)
            .SingleOrDefaultAsync(cancellationToken);
        if (patternId is null)
        {
            return null;
        }

        var stops = await db.PatternStops.AsNoTracking()
            .Where(s => s.PatternId == patternId)
            .OrderBy(s => s.Sequence)
            .Join(db.Stations, s => s.StationId, st => st.Id, (s, st) => new RailStop(
                s.Id, s.StationId, st.NameEn, s.Sequence, s.ArrivalLocal != null, s.PlannedPlatform))
            .ToListAsync(cancellationToken);
        return new RailJourney(journeyId, stops);
    }

    public async Task<IReadOnlyList<Guid>> AllJourneyIdsAsync(CancellationToken cancellationToken) =>
        await db.Journeys.AsNoTracking().Select(j => j.Id).ToListAsync(cancellationToken);
}
