using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Rail.Running;

// Works out the running answer for one journey at a given time: the schedule, the newest
// observation and the staff decisions in force. The public running read and the protected
// briefing both use it, so they cannot disagree about what is current.
public sealed class RunningReader(SmartRailDbContext db, IConfirmedChangeReader changes)
{
    // Null for an unknown journey.
    public async Task<RunningResponse?> ReadAsync(Guid journeyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var journey = await db.Journeys
            .AsNoTracking()
            .Include(j => j.Pattern).ThenInclude(p => p.Stops)
            .SingleOrDefaultAsync(j => j.Id == journeyId, cancellationToken);
        if (journey is null)
        {
            return null;
        }

        var observation = await ObservationIngestion.CurrentAsync(db, journeyId, cancellationToken);
        var confirmed = await changes.ReadAsync(journeyId, cancellationToken);
        var state = RunningCalculator.Calculate(journey, observation?.ToRunning(), now, overrides: confirmed);
        return RunningResponses.Build(journey, state, observation);
    }
}
