using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Operations.Incidents;

// Reads the incident table. Every read goes to the database; nothing is cached between requests.
public sealed class IncidentStore(SmartRailDbContext db)
{
    public Task<Incident?> FindAsync(Guid incidentId, CancellationToken cancellationToken) =>
        db.Incidents.AsNoTracking().SingleOrDefaultAsync(i => i.Id == incidentId, cancellationToken);

    public Task<Incident?> FindBySourceAsync(Guid sourceReportId, CancellationToken cancellationToken) =>
        db.Incidents.AsNoTracking().SingleOrDefaultAsync(i => i.SourceReportId == sourceReportId, cancellationToken);

    // Newest first, optionally only Open or only Resolved. One more than size comes back so the
    // caller can tell whether another page exists.
    public async Task<List<Incident>> ListAsync(string? state, PageCursor? after, int size, CancellationToken cancellationToken)
    {
        var query = db.Incidents.AsNoTracking();
        if (state is not null)
        {
            query = query.Where(i => i.Status == state);
        }

        if (after is not null)
        {
            var (time, id) = after;
            query = query.Where(i => i.OpenedAt < time || (i.OpenedAt == time && i.Id.CompareTo(id) < 0));
        }

        return await query.OrderByDescending(i => i.OpenedAt).ThenByDescending(i => i.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken);
    }

    // The incidents that started from a report sent on this journey.
    public Task<List<Guid>> IdsForJourneyAsync(Guid journeyId, CancellationToken cancellationToken) =>
        db.Incidents.AsNoTracking()
            .Where(i => i.SourceReportId != null
                && db.DriverReports.Any(r => r.Id == i.SourceReportId && r.JourneyId == journeyId))
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
}
