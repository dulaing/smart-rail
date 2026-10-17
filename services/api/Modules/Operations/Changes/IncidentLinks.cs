using SmartRail.Api.Modules.Operations.Incidents;

namespace SmartRail.Api.Modules.Operations.Changes;

// Decides whether a new change may name an incident. A change is only ever explained by an open
// incident that fits its journey: a report-backed incident covers just the journey its report was
// sent on, and a standalone incident may explain changes on several journeys.
public sealed class IncidentLinks(IncidentStore incidents)
{
    // Null when the link is acceptable.
    public async Task<ChangeProblem?> CheckForPublishAsync(Guid journeyId, Guid incidentId, CancellationToken cancellationToken)
    {
        if (await incidents.FindLinkTargetAsync(incidentId, cancellationToken) is not { } target)
        {
            return new ChangeProblem(ProblemKind.NotFound, "No incident has this ID.");
        }

        if (target.Status != IncidentStates.Open)
        {
            return new ChangeProblem(
                ProblemKind.Invalid, "The incident is resolved, so it cannot explain a new change. Open a new incident for it.");
        }

        return target.SourceJourneyId is { } reportJourney && reportJourney != journeyId
            ? new ChangeProblem(ProblemKind.Invalid, "The incident came from a report on another journey.")
            : null;
    }
}
