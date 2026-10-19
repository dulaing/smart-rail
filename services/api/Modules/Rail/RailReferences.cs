using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Rail;

// The small door other modules use to check that a station or a dated journey really exists,
// without reading Rail's timetable tables. Access validates a save or follow through this.
public interface IRailReferenceReader
{
    Task<bool> StationExistsAsync(Guid stationId, CancellationToken cancellationToken);

    Task<bool> JourneyExistsAsync(Guid journeyId, CancellationToken cancellationToken);
}

public sealed class RailReferenceReader(SmartRailDbContext db) : IRailReferenceReader
{
    public Task<bool> StationExistsAsync(Guid stationId, CancellationToken cancellationToken) =>
        db.Stations.AsNoTracking().AnyAsync(s => s.Id == stationId, cancellationToken);

    public Task<bool> JourneyExistsAsync(Guid journeyId, CancellationToken cancellationToken) =>
        db.Journeys.AsNoTracking().AnyAsync(j => j.Id == journeyId, cancellationToken);
}
