using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Rail.Running;

// What a guest sees for GET /api/journeys/{id}/running. Nothing here changes just because time
// passes (no "age" and no server time), so the response hash only moves when what it says moves:
// new evidence, a freshness threshold being crossed, or a changed prediction.
public sealed record RunningResponse(
    Guid JourneyId,
    DateOnly ServiceDate,
    string TrainNumber,
    TimetableSource Timetable,
    string RunningLabel,
    FreshnessThresholds Thresholds,
    string Freshness,
    LastObservationSummary? LastObservation,
    CurrentPosition? CurrentPosition,
    int? DelaySeconds,
    string Availability,
    int? ConfirmedDelayMinutes,
    Guid? FinalStopId,
    IReadOnlyList<StopRunning> Stops);

// Where the schedule came from. It is the timetable's label, never a claim about live running.
public sealed record TimetableSource(string Kind, string Label, string? Url, DateTimeOffset? CheckedAt);

// Tells clients when to start calling the evidence ageing and unknown.
public sealed record FreshnessThresholds(int AgeingAfterSeconds, int UnknownAfterSeconds);

// The newest report, kept even when it is too old to trust, so a client can show how old it is.
public sealed record LastObservationSummary(
    DateTimeOffset ObservedAt,
    DateTimeOffset ReceivedAt,
    Guid FromStopId,
    Guid ToStopId,
    int ProgressPermille,
    int ReportedDelaySeconds,
    Guid? LastKnownStationId);

public static class RunningResponses
{
    // Every running answer is simulated, even for a journey whose timetable is published.
    public const string RunningLabel = "SmartRail simulation";

    public static RunningResponse Build(ScheduledJourney journey, RunningState state, Observation? observation)
    {
        var pattern = journey.Pattern;
        return new RunningResponse(
            journey.Id,
            journey.ServiceDate,
            pattern.TrainNumber,
            new TimetableSource(pattern.SourceKind.ToCode(), pattern.SourceKind.Label(), pattern.SourceUrl, pattern.SourceCheckedAt),
            RunningLabel,
            new FreshnessThresholds(FreshnessRules.AgeingAfterSeconds, FreshnessRules.UnknownAfterSeconds),
            state.Freshness.ToCode(),
            observation is null ? null : Summarise(observation, state.Stops),
            state.CurrentPosition,
            state.DelaySeconds,
            state.Availability,
            state.ConfirmedDelayMinutes,
            state.FinalStopId,
            state.Stops);
    }

    // The station the train last stood at or passed: the station it has just reached, or else the
    // one it left.
    private static LastObservationSummary Summarise(Observation o, IReadOnlyList<StopRunning> stops)
    {
        var lastStopId = o.ProgressPermille == RunningCalculator.FullProgress ? o.ToStopId : o.FromStopId;
        var station = stops.FirstOrDefault(s => s.StopId == lastStopId)?.StationId;
        return new LastObservationSummary(
            o.ObservedAt, o.ReceivedAt, o.FromStopId, o.ToStopId, o.ProgressPermille, o.ReportedDelaySeconds, station);
    }
}
