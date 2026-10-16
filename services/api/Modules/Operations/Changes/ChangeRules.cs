namespace SmartRail.Api.Modules.Operations.Changes;

public enum ProblemKind
{
    // Malformed or inapplicable: HTTP 400.
    Invalid,

    // The thing named does not exist for this journey: HTTP 404.
    NotFound,
}

// A rule failure. The message is safe to show the caller.
public sealed record ChangeProblem(ProblemKind Kind, string Message);

// A value or the reason there is none.
public sealed class Outcome<T>
{
    private Outcome(T? value, ChangeProblem? problem)
    {
        Value = value;
        Problem = problem;
    }

    public T? Value { get; }

    public ChangeProblem? Problem { get; }

    public bool Succeeded => Problem is null;

    public static Outcome<T> Ok(T value) => new(value, null);

    public static Outcome<T> Invalid(string message) => new(default, new ChangeProblem(ProblemKind.Invalid, message));

    public static Outcome<T> NotFound(string message) => new(default, new ChangeProblem(ProblemKind.NotFound, message));
}

// What a staff client may ask for. No database, HTTP or clock: the caller supplies the
// journey's stops and the change history.
public static class ChangeRules
{
    // Checks a draft against its kind and against this journey's own stop occurrences.
    public static Outcome<ChangeDetails> ValidateDraft(ChangeDraft? draft, OperationsJourney journey)
    {
        if (draft is null)
        {
            return Outcome<ChangeDetails>.Invalid("A JSON change body is required.");
        }

        if (!ChangeCodes.TryParseKind(draft.Kind, out var kind))
        {
            return Outcome<ChangeDetails>.Invalid("kind is required and must be DELAY, CANCEL, PLATFORM or EARLY_END.");
        }

        var unrelated = UnrelatedFields(draft, kind);
        if (unrelated.Count > 0)
        {
            return Outcome<ChangeDetails>.Invalid(
                $"{string.Join(", ", unrelated)} does not apply to a {kind.ToCode()} change.");
        }

        return kind switch
        {
            ChangeKind.Delay => ValidateDelay(draft),
            ChangeKind.Cancel => Outcome<ChangeDetails>.Ok(ChangeDetails.Cancel()),
            ChangeKind.Platform => ValidatePlatform(draft, journey),
            _ => ValidateEarlyEnd(draft, journey),
        };
    }

    // A withdrawal must name a publish of this journey that still counts: not already withdrawn
    // and not replaced by a newer change of the same kind (or, for a platform, the same stop).
    public static Outcome<ServiceChange> ValidateWithdrawal(
        Guid? changeId, IReadOnlyList<ServiceChange> history, ConfirmedState state)
    {
        if (changeId is not { } id || id == Guid.Empty)
        {
            return Outcome<ServiceChange>.Invalid("changeId is required.");
        }

        var change = history.FirstOrDefault(c => c.Id == id);
        if (change is null)
        {
            return Outcome<ServiceChange>.NotFound("No such change for this journey.");
        }

        if (change.Action != ChangeAction.Publish)
        {
            return Outcome<ServiceChange>.Invalid("Only a published change can be withdrawn.");
        }

        if (!state.IsActive(change))
        {
            return Outcome<ServiceChange>.Invalid("That change has already been withdrawn or replaced.");
        }

        return Outcome<ServiceChange>.Ok(change);
    }

    private static Outcome<ChangeDetails> ValidateDelay(ChangeDraft draft) =>
        draft.DelayMinutes is { } minutes and >= 0
            ? Outcome<ChangeDetails>.Ok(ChangeDetails.Delay(minutes))
            : Outcome<ChangeDetails>.Invalid("delayMinutes is required and must be a whole number of minutes, zero or more.");

    private static Outcome<ChangeDetails> ValidatePlatform(ChangeDraft draft, OperationsJourney journey)
    {
        if (string.IsNullOrWhiteSpace(draft.NewPlatform))
        {
            return Outcome<ChangeDetails>.Invalid("newPlatform is required and must not be blank.");
        }

        if (draft.TargetStopId is not { } target)
        {
            return Outcome<ChangeDetails>.Invalid("targetStopId is required.");
        }

        return journey.IndexOf(target) < 0
            ? Outcome<ChangeDetails>.Invalid("targetStopId must be a stop occurrence of this journey.")
            : Outcome<ChangeDetails>.Ok(ChangeDetails.Platform(target, draft.NewPlatform.Trim()));
    }

    // An early ending moves the final stop earlier, so it needs a real stop of the train that
    // is neither where it starts nor where it already ends, and one it is scheduled to arrive at.
    private static Outcome<ChangeDetails> ValidateEarlyEnd(ChangeDraft draft, OperationsJourney journey)
    {
        if (draft.FinalStopId is not { } final)
        {
            return Outcome<ChangeDetails>.Invalid("finalStopId is required.");
        }

        var index = journey.IndexOf(final);
        if (index < 0)
        {
            return Outcome<ChangeDetails>.Invalid("finalStopId must be a stop occurrence of this journey.");
        }

        if (index == 0 || index == journey.Stops.Count - 1)
        {
            return Outcome<ChangeDetails>.Invalid("finalStopId must be an intermediate stop, not the first or last stop.");
        }

        return journey.Stops[index].HasScheduledArrival
            ? Outcome<ChangeDetails>.Ok(ChangeDetails.EarlyEnd(final))
            : Outcome<ChangeDetails>.Invalid("finalStopId must be a stop with a scheduled arrival.");
    }

    private static List<string> UnrelatedFields(ChangeDraft draft, ChangeKind kind)
    {
        var unrelated = new List<string>();
        if (draft.DelayMinutes is not null && kind != ChangeKind.Delay)
        {
            unrelated.Add("delayMinutes");
        }

        if (draft.NewPlatform is not null && kind != ChangeKind.Platform)
        {
            unrelated.Add("newPlatform");
        }

        if (draft.TargetStopId is not null && kind != ChangeKind.Platform)
        {
            unrelated.Add("targetStopId");
        }

        if (draft.FinalStopId is not null && kind != ChangeKind.EarlyEnd)
        {
            unrelated.Add("finalStopId");
        }

        return unrelated;
    }
}
