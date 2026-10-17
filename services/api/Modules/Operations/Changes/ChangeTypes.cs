namespace SmartRail.Api.Modules.Operations.Changes;

// Stored as the codes DELAY, CANCEL, PLATFORM and EARLY_END.
public enum ChangeKind
{
    Delay,
    Cancel,
    Platform,
    EarlyEnd,
}

// Stored as the codes PUBLISH and WITHDRAW.
public enum ChangeAction
{
    Publish,
    Withdraw,
}

public static class ChangeCodes
{
    public static string ToCode(this ChangeKind kind) => kind switch
    {
        ChangeKind.Delay => "DELAY",
        ChangeKind.Cancel => "CANCEL",
        ChangeKind.Platform => "PLATFORM",
        ChangeKind.EarlyEnd => "EARLY_END",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParseKind(string? code, out ChangeKind kind)
    {
        switch (code)
        {
            case "DELAY":
                kind = ChangeKind.Delay;
                return true;
            case "CANCEL":
                kind = ChangeKind.Cancel;
                return true;
            case "PLATFORM":
                kind = ChangeKind.Platform;
                return true;
            case "EARLY_END":
                kind = ChangeKind.EarlyEnd;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static string ToCode(this ChangeAction action) => action switch
    {
        ChangeAction.Publish => "PUBLISH",
        ChangeAction.Withdraw => "WITHDRAW",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };
}

// The JSON body of a change as a staff client writes it. Everything is nullable so a missing
// field is a clear 400, and so a field that does not belong to the kind can be rejected.
public sealed record ChangeDraft(
    string? Kind,
    int? DelayMinutes,
    string? NewPlatform,
    Guid? TargetStopId,
    Guid? FinalStopId);

// A change whose own fields are valid for its kind. Only the fields of that kind are set.
// Both stop IDs are pattern-stop occurrences of the journey, never station IDs.
public sealed record ChangeDetails(
    ChangeKind Kind,
    int? DelayMinutes = null,
    string? NewPlatform = null,
    Guid? TargetStopId = null,
    Guid? FinalStopId = null)
{
    public static ChangeDetails Delay(int minutes) => new(ChangeKind.Delay, DelayMinutes: minutes);

    public static ChangeDetails Cancel() => new(ChangeKind.Cancel);

    public static ChangeDetails Platform(Guid targetStopId, string newPlatform) =>
        new(ChangeKind.Platform, NewPlatform: newPlatform, TargetStopId: targetStopId);

    public static ChangeDetails EarlyEnd(Guid finalStopId) => new(ChangeKind.EarlyEnd, FinalStopId: finalStopId);
}

// One row of changes.service_changes. History is only ever appended to. A withdrawal carries
// the kind of the change it withdraws, no type-specific fields, and points at that change.
// JourneyId, PublishedById, PublishedAt and IncidentId are set by the server when a row is stored.
// A withdrawal keeps the incident of the change it withdraws, even if that incident is resolved.
public sealed class ServiceChange
{
    private ServiceChange()
    {
    }

    public ServiceChange(Guid id, int journeyVersion, ChangeAction action, ChangeDetails details, Guid? supersedesChangeId = null)
    {
        Id = id;
        JourneyVersion = journeyVersion;
        Action = action;
        Kind = details.Kind;
        DelayMinutes = details.DelayMinutes;
        NewPlatform = details.NewPlatform;
        TargetStopId = details.TargetStopId;
        FinalStopId = details.FinalStopId;
        SupersedesChangeId = supersedesChangeId;
    }

    public Guid Id { get; private set; }
    public Guid JourneyId { get; init; }
    public int JourneyVersion { get; private set; }
    public ChangeAction Action { get; private set; }
    public ChangeKind Kind { get; private set; }
    public int? DelayMinutes { get; private set; }
    public string? NewPlatform { get; private set; }
    public Guid? TargetStopId { get; private set; }
    public Guid? FinalStopId { get; private set; }
    public Guid? SupersedesChangeId { get; private set; }

    // The incident that explains this change, if the controller linked one.
    public Guid? IncidentId { get; init; }

    public Guid PublishedById { get; init; }
    public DateTimeOffset PublishedAt { get; init; }

    public ChangeDetails Details => new(Kind, DelayMinutes, NewPlatform, TargetStopId, FinalStopId);
}

// What the rules need to know about one stop occurrence of a journey's pattern. Rail supplies
// these later; the rules never query Rail's tables.
public sealed record JourneyStop(
    Guid Id,
    Guid StationId,
    string StationName,
    int Sequence,
    bool HasScheduledArrival,
    string? PlannedPlatform);

public sealed class OperationsJourney
{
    public OperationsJourney(Guid id, IEnumerable<JourneyStop> stops)
    {
        Id = id;
        Stops = stops.OrderBy(s => s.Sequence).ToList();
    }

    public Guid Id { get; }

    // In running order. A station that the train visits twice appears twice, with two IDs.
    public IReadOnlyList<JourneyStop> Stops { get; }

    public int IndexOf(Guid stopId)
    {
        for (var i = 0; i < Stops.Count; i++)
        {
            if (Stops[i].Id == stopId)
            {
                return i;
            }
        }

        return -1;
    }
}
