using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Operations.Reports;

// Reads the report table. Every read goes to the database; nothing is cached between requests.
public sealed class ReportStore(SmartRailDbContext db)
{
    public Task<DriverReport?> FindByClientIdAsync(Guid clientReportId, CancellationToken cancellationToken) =>
        db.DriverReports.AsNoTracking().SingleOrDefaultAsync(r => r.ClientReportId == clientReportId, cancellationToken);

    // A report the driver sent on this journey, or null. Another driver's report looks the same
    // as a missing one, so its existence is not revealed.
    public Task<DriverReport?> FindOwnAsync(Guid reportId, Guid journeyId, Guid driverId, CancellationToken cancellationToken) =>
        db.DriverReports.AsNoTracking().SingleOrDefaultAsync(
            r => r.Id == reportId && r.JourneyId == journeyId && r.SubmittedById == driverId, cancellationToken);

    // The driver's own reports on one journey, newest first, one more than size so the caller
    // can tell whether another page exists.
    public async Task<List<DriverReport>> ListOwnAsync(
        Guid journeyId, Guid driverId, Guid? clientReportId, PageCursor? after, int size, CancellationToken cancellationToken)
    {
        var query = db.DriverReports.AsNoTracking().Where(r => r.JourneyId == journeyId && r.SubmittedById == driverId);
        if (clientReportId is { } key)
        {
            query = query.Where(r => r.ClientReportId == key);
        }

        if (after is not null)
        {
            var (time, id) = after;
            query = query.Where(r => r.ReceivedAt < time || (r.ReceivedAt == time && r.Id.CompareTo(id) < 0));
        }

        return await query.OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Guid>> IdsForJourneyAsync(Guid journeyId, CancellationToken cancellationToken) =>
        db.DriverReports.AsNoTracking().Where(r => r.JourneyId == journeyId).Select(r => r.Id).ToListAsync(cancellationToken);
}
