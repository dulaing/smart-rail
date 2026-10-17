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

        return await query.NewestFirstAfter(after).Take(size + 1).ToListAsync(cancellationToken);
    }

    public Task<DriverReport?> FindAsync(Guid reportId, CancellationToken cancellationToken) =>
        db.DriverReports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reportId, cancellationToken);

    // Control's inbox, newest first, optionally for one journey and only acknowledged or only
    // unacknowledged reports. One more than size comes back, as for a driver's list.
    public async Task<List<DriverReport>> ListInboxAsync(
        Guid? journeyId, bool? acknowledged, PageCursor? after, int size, CancellationToken cancellationToken)
    {
        var query = db.DriverReports.AsNoTracking();
        if (journeyId is { } journey)
        {
            query = query.Where(r => r.JourneyId == journey);
        }

        if (acknowledged is { } wanted)
        {
            query = query.Where(r => (r.AcknowledgedAt != null) == wanted);
        }

        return await query.NewestFirstAfter(after).Take(size + 1).ToListAsync(cancellationToken);
    }

    public Task<List<Guid>> IdsForJourneyAsync(Guid journeyId, CancellationToken cancellationToken) =>
        db.DriverReports.AsNoTracking().Where(r => r.JourneyId == journeyId).Select(r => r.Id).ToListAsync(cancellationToken);
}

internal static class ReportQueries
{
    // Newest first with the ID as the tie-break, starting strictly after the cursor. PostgreSQL
    // compares the IDs itself, so the order of pages matches the order inside each page.
    public static IQueryable<DriverReport> NewestFirstAfter(this IQueryable<DriverReport> query, PageCursor? after)
    {
        if (after is not null)
        {
            var (time, id) = after;
            query = query.Where(r => r.ReceivedAt < time || (r.ReceivedAt == time && r.Id.CompareTo(id) < 0));
        }

        return query.OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id);
    }
}
