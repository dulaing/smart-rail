using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Rail.Running;

public enum IngestStatus
{
    Created,
    Duplicate,
    Conflict,
    UnknownJourney,
    Invalid,
}

public sealed record IngestResult(IngestStatus Status, string? Detail = null, Observation? Observation = null);

// The one way an observation enters the database, used by the protected endpoint and, later,
// by the simulator so both go through the same checks.
public static class ObservationIngestion
{
    public static async Task<IngestResult> IngestAsync(
        SmartRailDbContext db, TimeProvider time, ObservationRequest? request, CancellationToken cancellationToken)
    {
        var receivedAt = time.GetUtcNow();
        if (!ObservationRules.TryRead(request, receivedAt, out var input, out var error))
        {
            return new IngestResult(IngestStatus.Invalid, error);
        }

        var journey = await db.Journeys
            .AsNoTracking()
            .Include(j => j.Pattern).ThenInclude(p => p.Stops)
            .SingleOrDefaultAsync(j => j.Id == input.JourneyId, cancellationToken);
        if (journey is null)
        {
            return new IngestResult(IngestStatus.UnknownJourney, "journeyId is not a known journey.");
        }

        if (!ObservationRules.IsSegmentOf(input, journey.Pattern.Stops))
        {
            return new IngestResult(IngestStatus.Invalid,
                "fromStopId and toStopId must be neighbouring stops of this journey, in order.");
        }

        // The unique index on source_event_id decides, so concurrent retries cannot both insert.
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO live.observations
                (id, journey_id, source_event_id, from_stop_id, to_stop_id,
                 progress_permille, reported_delay_seconds, observed_at, received_at)
            VALUES
                ({Guid.NewGuid()}, {input.JourneyId}, {input.SourceEventId}, {input.FromStopId}, {input.ToStopId},
                 {input.ProgressPermille}, {input.ReportedDelaySeconds}, {input.ObservedAt}, {receivedAt.ToUniversalTime()})
            ON CONFLICT (source_event_id) DO NOTHING
            """, cancellationToken);

        var stored = await db.Observations.AsNoTracking()
            .SingleAsync(o => o.SourceEventId == input.SourceEventId, cancellationToken);

        if (inserted == 1)
        {
            return new IngestResult(IngestStatus.Created, Observation: stored);
        }

        return ObservationRules.IsSameEvent(stored, input)
            ? new IngestResult(IngestStatus.Duplicate, Observation: stored)
            : new IngestResult(IngestStatus.Conflict, "sourceEventId was already used for a different observation.");
    }

    // Current evidence for a journey: the greatest observed_at, with older reports left as history.
    public static async Task<Observation?> CurrentAsync(
        SmartRailDbContext db, Guid journeyId, CancellationToken cancellationToken)
    {
        var latest = await db.Observations.AsNoTracking()
            .Where(o => o.JourneyId == journeyId)
            .MaxAsync(o => (DateTimeOffset?)o.ObservedAt, cancellationToken);
        if (latest is null)
        {
            return null;
        }

        var atLatest = await db.Observations.AsNoTracking()
            .Where(o => o.JourneyId == journeyId && o.ObservedAt == latest)
            .ToListAsync(cancellationToken);
        return ObservationRules.Current(atLatest);
    }
}
