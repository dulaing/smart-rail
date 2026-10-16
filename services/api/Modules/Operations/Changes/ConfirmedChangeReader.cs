using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Operations.Changes;

// Gives Rail the decisions in force for a journey. A journey Operations does not know has none.
public sealed class ConfirmedChangeReader(ChangeStore store) : IConfirmedChangeReader
{
    public async Task<ConfirmedOverrides> ReadAsync(Guid journeyId, CancellationToken cancellationToken)
    {
        var changes = await store.LoadAsync(journeyId, cancellationToken);
        return changes is null ? ConfirmedOverrides.None : From(ConfirmedState.Fold(changes.History));
    }

    public static ConfirmedOverrides From(ConfirmedState state) =>
        new(
            state.Delay?.DelayMinutes,
            state.Cancel is not null,
            state.EarlyEnd?.FinalStopId,
            state.Active
                .Where(c => c.Kind == ChangeKind.Platform)
                .ToDictionary(c => c.TargetStopId!.Value, c => new ConfirmedPlatform(c.NewPlatform!, c.PublishedAt)));
}
