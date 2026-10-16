using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Rail.Running;

// How much a simulated observation can be trusted, from its age. Stored nowhere: it is
// recalculated from the observation time whenever it is asked for.
public enum Freshness
{
    Current,
    Ageing,
    Unknown,
}

public static class FreshnessRules
{
    // Scope decision D5. The server supplies these so every client uses the same values.
    public const int AgeingAfterSeconds = 180;
    public const int UnknownAfterSeconds = 240;

    // Age is the time since the train was observed, never since we received or polled it.
    public static Freshness Classify(DateTimeOffset? observedAt, DateTimeOffset now)
    {
        if (observedAt is not { } at)
        {
            return Freshness.Unknown;
        }

        var age = now - at;
        if (age >= TimeSpan.FromSeconds(UnknownAfterSeconds))
        {
            return Freshness.Unknown;
        }

        return age >= TimeSpan.FromSeconds(AgeingAfterSeconds) ? Freshness.Ageing : Freshness.Current;
    }

    // The codes clients see and the shared test vectors use.
    public static string ToCode(this Freshness freshness) => freshness switch
    {
        Freshness.Current => "CURRENT",
        Freshness.Ageing => "AGEING",
        _ => "UNKNOWN",
    };
}

// One report of where the train was: on the segment between two neighbouring stop
// occurrences, ProgressPermille of the way along it (1000 = at the To stop).
public sealed record RunningObservation(
    Guid FromStopId,
    Guid ToStopId,
    int ProgressPermille,
    int ReportedDelaySeconds,
    DateTimeOffset ObservedAt);

public sealed record CurrentPosition(Guid FromStopId, Guid ToStopId, int ProgressPermille);

// One stop occurrence with its schedule and, while the evidence is usable, its estimates.
// An estimate is null for a time that has already happened.
public sealed record StopRunning(
    Guid StopId,
    Guid StationId,
    int Sequence,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture,
    string? PlannedPlatform,
    DateTimeOffset? EstimatedArrival,
    DateTimeOffset? EstimatedDeparture);

// The calculated running picture of one journey. The schedule and the last observation
// are always present; position, delay and estimates are null once the evidence is unknown.
public sealed record RunningState(
    Freshness Freshness,
    RunningObservation? LastObservation,
    CurrentPosition? CurrentPosition,
    int? DelaySeconds,
    IReadOnlyList<StopRunning> Stops);
