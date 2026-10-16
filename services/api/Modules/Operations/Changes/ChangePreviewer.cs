namespace SmartRail.Api.Modules.Operations.Changes;

public static class Surfaces
{
    public const string PassengerApp = "PASSENGER_APP";
    public const string StationBoard = "STATION_BOARD";
    public const string DriverBriefing = "DRIVER_BRIEFING";
}

// A place people read the journey. StationIds is set for station boards and says whose.
public sealed record ScreenImpact(string Surface, IReadOnlyList<Guid> StationIds);

// What the change is expected to do to food orders. AffectedOrderCount is null when Catering
// could not supply one: unknown, never zero. It is zero only when nothing food-related changes.
public sealed record FoodImpact(
    bool OrderClosedBefore,
    bool OrderClosedAfter,
    IReadOnlyList<Guid> NewlyBlockedStopIds,
    IReadOnlyList<Guid> ReopenedStopIds,
    bool EtaChanges,
    int? AffectedOrderCount);

// Where an early ending leaves the train, and the later stops it no longer serves.
public sealed record EarlyEndImpact(
    Guid FinalStopId,
    Guid FinalStationId,
    string FinalStationName,
    IReadOnlyList<StopOutcome> UnservedStops);

// What a controller reviews before confirming. Withdrawals describe the change being withdrawn
// in Change. Nothing here is stored, versioned or sent anywhere.
public sealed record ChangePreview(
    ChangeAction Action,
    ChangeDetails Change,
    Guid? WithdrawsChangeId,
    int PreviewedVersion,
    JourneyOutcome Before,
    JourneyOutcome After,
    IReadOnlyList<StopOutcome> AffectedStops,
    IReadOnlyList<ScreenImpact> Screens,
    FoodImpact Food,
    EarlyEndImpact? EarlyEnd);

// Works out the confirmed state a change would produce without writing it. The caller reads the
// journey version and history together and passes the version through as previewedVersion;
// publishing recomputes all of this rather than trusting a client's copy.
public static class ChangePreviewer
{
    public static Outcome<ChangePreview> PreviewPublish(
        OperationsJourney journey,
        int journeyVersion,
        IReadOnlyList<ServiceChange> history,
        ChangeDraft? draft,
        int? cateringOrderCount = null)
    {
        var checkedDraft = ChangeRules.ValidateDraft(draft, journey);
        if (checkedDraft.Problem is { } problem)
        {
            return Fail(problem);
        }

        var details = checkedDraft.Value!;
        var change = new ServiceChange(Guid.Empty, journeyVersion + 1, ChangeAction.Publish, details);
        return Outcome<ChangePreview>.Ok(
            Build(journey, journeyVersion, history, change, null, details, cateringOrderCount));
    }

    public static Outcome<ChangePreview> PreviewWithdrawal(
        OperationsJourney journey,
        int journeyVersion,
        IReadOnlyList<ServiceChange> history,
        Guid? changeId,
        int? cateringOrderCount = null)
    {
        var target = ChangeRules.ValidateWithdrawal(changeId, history, ConfirmedState.Fold(history));
        if (target.Problem is { } problem)
        {
            return Fail(problem);
        }

        var withdrawn = target.Value!;
        var change = new ServiceChange(
            Guid.Empty,
            journeyVersion + 1,
            ChangeAction.Withdraw,
            new ChangeDetails(withdrawn.Details.Kind),
            withdrawn.Id);
        return Outcome<ChangePreview>.Ok(
            Build(journey, journeyVersion, history, change, withdrawn.Id, withdrawn.Details, cateringOrderCount));
    }

    private static Outcome<ChangePreview> Fail(ChangeProblem problem) =>
        problem.Kind == ProblemKind.NotFound
            ? Outcome<ChangePreview>.NotFound(problem.Message)
            : Outcome<ChangePreview>.Invalid(problem.Message);

    private static ChangePreview Build(
        OperationsJourney journey,
        int journeyVersion,
        IReadOnlyList<ServiceChange> history,
        ServiceChange next,
        Guid? withdrawsChangeId,
        ChangeDetails shown,
        int? cateringOrderCount)
    {
        var before = JourneyOutcome.From(journey, ConfirmedState.Fold(history));
        var after = JourneyOutcome.From(journey, ConfirmedState.Fold([.. history, next]));

        var affected = after.Stops.Where((stop, i) => stop != before.Stops[i]).ToList();

        var earlyEnd = next.Action == ChangeAction.Publish && next.Details.Kind == ChangeKind.EarlyEnd
            ? EarlyEndOf(journey, after)
            : null;

        return new ChangePreview(
            next.Action,
            shown,
            withdrawsChangeId,
            journeyVersion,
            before,
            after,
            affected,
            ScreensFor(journey, before, after, affected),
            FoodFor(before, after, cateringOrderCount),
            earlyEnd);
    }

    private static EarlyEndImpact? EarlyEndOf(OperationsJourney journey, JourneyOutcome after)
    {
        if (after.FinalStopId is not { } finalStopId)
        {
            return null;
        }

        var final = journey.Stops[journey.IndexOf(finalStopId)];
        var later = after.Stops.Where(s => after.BlockedPickupStopIds.Contains(s.StopId)).ToList();
        return new EarlyEndImpact(final.Id, final.StationId, final.StationName, later);
    }

    // Every surface that shows the journey changes together; only boards differ by station.
    // A board also changes where the train starts or stops ending, though that stop still serves.
    private static List<ScreenImpact> ScreensFor(
        OperationsJourney journey, JourneyOutcome before, JourneyOutcome after, List<StopOutcome> affected)
    {
        var changed = affected.Count > 0
            || before.Availability != after.Availability
            || before.ConfirmedDelayMinutes != after.ConfirmedDelayMinutes
            || before.FinalStopId != after.FinalStopId;
        if (!changed)
        {
            return [];
        }

        var stations = affected.Select(s => s.StationId).ToList();
        foreach (var endingStopId in new[] { before.FinalStopId, after.FinalStopId }.OfType<Guid>())
        {
            stations.Add(journey.Stops[journey.IndexOf(endingStopId)].StationId);
        }

        return
        [
            new ScreenImpact(Surfaces.PassengerApp, []),
            new ScreenImpact(Surfaces.StationBoard, stations.Distinct().ToList()),
            new ScreenImpact(Surfaces.DriverBriefing, []),
        ];
    }

    private static FoodImpact FoodFor(JourneyOutcome before, JourneyOutcome after, int? cateringOrderCount)
    {
        var newlyBlocked = after.BlockedPickupStopIds.Except(before.BlockedPickupStopIds).ToList();
        var reopened = before.BlockedPickupStopIds.Except(after.BlockedPickupStopIds).ToList();
        var etaChanges = before.ConfirmedDelayMinutes != after.ConfirmedDelayMinutes;

        var foodChanges = newlyBlocked.Count > 0
            || reopened.Count > 0
            || etaChanges
            || before.OrderClosed != after.OrderClosed;

        return new FoodImpact(
            before.OrderClosed,
            after.OrderClosed,
            newlyBlocked,
            reopened,
            etaChanges,
            foodChanges ? cateringOrderCount : 0);
    }
}
