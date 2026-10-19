using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Operations.Briefings;

// One assigned journey in the driver's list for a service date.
public sealed record DriverJourneyDto(
    Guid JourneyId,
    DateOnly ServiceDate,
    string TrainNumber,
    string TimetableLabel,
    string OriginName,
    DateTimeOffset? ScheduledDeparture,
    string DestinationName,
    DateTimeOffset? ScheduledArrival,
    string Availability)
{
    public static DriverJourneyDto From(JourneySummary journey, string availability) =>
        new(
            journey.JourneyId, journey.ServiceDate, journey.TrainNumber, journey.TimetableLabel,
            journey.OriginName, journey.ScheduledDeparture, journey.DestinationName, journey.ScheduledArrival,
            availability);
}

public sealed record DriverJourneysDto(DateOnly ServiceDate, IReadOnlyList<DriverJourneyDto> Journeys);

public sealed record BriefingTimetableDto(string Kind, string Label, DateTimeOffset? CheckedAt);

public sealed record BriefingThresholdsDto(int AgeingAfterSeconds, int UnknownAfterSeconds);

// The newest report of where the train was, kept even when too old to trust so the age can show.
public sealed record BriefingEvidenceDto(DateTimeOffset ObservedAt, Guid? LastKnownStationId);

public sealed record BriefingPositionDto(Guid FromStopId, Guid ToStopId, int ProgressPermille);

public sealed record BriefingStopDto(
    Guid StopId,
    Guid StationId,
    string StationName,
    int Sequence,
    bool Served,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture,
    DateTimeOffset? EstimatedArrival,
    DateTimeOffset? EstimatedDeparture,
    string? PlannedPlatform,
    string? ConfirmedPlatform,
    DateTimeOffset? ConfirmedPlatformAt);

// A staff decision in force. Unlike the public change, the driver sees who confirmed it. The name
// is null when it cannot be found; the client says so rather than inventing one. No audit
// history, incident link or account ID leaves with it.
public sealed record BriefingChangeDto(
    Guid ChangeId,
    string Kind,
    DateTimeOffset ConfirmedAt,
    string? ConfirmedByName,
    int? DelayMinutes,
    string? NewPlatform,
    Guid? TargetStopId,
    Guid? FinalStopId);

// The briefing for one assigned journey: the timetable, the running evidence with its thresholds
// and the confirmed changes, kept as separate facts. Nothing in it changes just because time
// passes, so the ETag moves only when the picture does; the client ages the evidence itself.
public sealed record DriverBriefingDto(
    Guid JourneyId,
    DateOnly ServiceDate,
    string TrainNumber,
    BriefingTimetableDto Timetable,
    string RunningLabel,
    BriefingThresholdsDto Thresholds,
    string Freshness,
    BriefingEvidenceDto? LastObservation,
    BriefingPositionDto? CurrentPosition,
    int? DelaySeconds,
    string Availability,
    int? ConfirmedDelayMinutes,
    Guid? FinalStopId,
    int JourneyVersion,
    IReadOnlyList<BriefingStopDto> Stops,
    IReadOnlyList<BriefingChangeDto> Changes)
{
    public static DriverBriefingDto From(
        RunningDetail running, int version, IReadOnlyList<ServiceChange> active, IReadOnlyDictionary<Guid, string> names) =>
        new(
            running.JourneyId,
            running.ServiceDate,
            running.TrainNumber,
            new BriefingTimetableDto(running.TimetableKind, running.TimetableLabel, running.TimetableCheckedAt),
            running.RunningLabel,
            new BriefingThresholdsDto(running.AgeingAfterSeconds, running.UnknownAfterSeconds),
            running.Freshness,
            running.ObservedAt is { } observedAt ? new BriefingEvidenceDto(observedAt, running.LastKnownStationId) : null,
            running.PositionFromStopId is { } from && running.PositionToStopId is { } to && running.ProgressPermille is { } progress
                ? new BriefingPositionDto(from, to, progress)
                : null,
            running.DelaySeconds,
            running.Availability,
            running.ConfirmedDelayMinutes,
            running.FinalStopId,
            version,
            running.Stops.Select(s => new BriefingStopDto(
                s.StopId, s.StationId, s.StationName, s.Sequence, s.Served,
                s.ScheduledArrival, s.ScheduledDeparture, s.EstimatedArrival, s.EstimatedDeparture,
                s.PlannedPlatform, s.ConfirmedPlatform, s.ConfirmedPlatformAt)).ToList(),
            active.Select(c => new BriefingChangeDto(
                c.Id, c.Kind.ToCode(), c.PublishedAt, names.GetValueOrDefault(c.PublishedById),
                c.DelayMinutes, c.NewPlatform, c.TargetStopId, c.FinalStopId)).ToList());
}
