using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Operations.Changes;

// Projects the changes in force for a set of journeys from Operations' append-only history.
// There is no alert, read-state or history table: a withdrawal empties a slot, so the projection
// simply stops returning that change, and a replacement returns only the change now in force.
public sealed class PersonalChangeReader(ChangeStore store, IJourneyStopReader journeys) : IPersonalChangeReader
{
    public async Task<IReadOnlyList<PersonalJourneyChanges>> ReadAsync(
        IReadOnlyList<Guid> journeyIds, CancellationToken cancellationToken)
    {
        var result = new List<PersonalJourneyChanges>();
        foreach (var journeyId in journeyIds.Distinct())
        {
            var changes = await store.LoadAsync(journeyId, cancellationToken);
            if (changes is null)
            {
                continue;
            }

            var journey = await journeys.FindAsync(journeyId, cancellationToken);
            var state = ConfirmedState.Fold(changes.History);
            var active = state.Active
                .Select(change => new PersonalChange(
                    change.Id,
                    change.Kind.ToCode(),
                    change.PublishedAt,
                    AffectedStops(journey, change)))
                .ToList();
            result.Add(new PersonalJourneyChanges(journeyId, changes.Version, active));
        }

        return result;
    }

    // The stop occurrences a decision controls. A cancellation and a delay touch the whole
    // journey; a platform change only its stop; an early ending the final stop and everything
    // after it that is no longer served.
    private static IReadOnlyList<Guid> AffectedStops(RailJourney? journey, ServiceChange change)
    {
        if (journey is null)
        {
            return [];
        }

        return change.Kind switch
        {
            ChangeKind.Platform => change.TargetStopId is { } target ? [target] : [],
            ChangeKind.EarlyEnd => EndingStops(journey, change.FinalStopId),
            _ => journey.Stops.Select(s => s.StopId).ToList(),
        };
    }

    private static IReadOnlyList<Guid> EndingStops(RailJourney journey, Guid? finalStopId)
    {
        if (finalStopId is not { } final)
        {
            return [];
        }

        var index = journey.Stops.ToList().FindIndex(s => s.StopId == final);
        return index < 0 ? [] : journey.Stops.Skip(index).Select(s => s.StopId).ToList();
    }
}
