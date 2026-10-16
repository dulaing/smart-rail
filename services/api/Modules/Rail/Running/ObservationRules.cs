using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Rail.Running;

// The JSON body of an observation as a sender writes it. Everything is nullable so a missing
// field is reported as a clear 400 rather than silently becoming zero.
public sealed record ObservationRequest(
    string? SourceEventId,
    Guid? JourneyId,
    Guid? FromStopId,
    Guid? ToStopId,
    int? ProgressPermille,
    int? ReportedDelaySeconds,
    string? ObservedAt);

// A request whose own fields are valid. ObservedAt is a UTC instant.
public sealed record ObservationInput(
    string SourceEventId,
    Guid JourneyId,
    Guid FromStopId,
    Guid ToStopId,
    int ProgressPermille,
    int ReportedDelaySeconds,
    DateTimeOffset ObservedAt);

// What may be accepted as an observation. No database or HTTP: the caller supplies the time
// and the journey's stops.
public static class ObservationRules
{
    public const int MaxEventIdLength = 100;

    // Checks that need only the request and the clock. The error is safe to show the sender.
    public static bool TryRead(
        ObservationRequest? request, DateTimeOffset now, out ObservationInput input, out string error)
    {
        input = null!;

        if (request is null)
        {
            error = "A JSON observation body is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.SourceEventId) || request.SourceEventId.Length > MaxEventIdLength)
        {
            error = $"sourceEventId is required and must be at most {MaxEventIdLength} characters.";
            return false;
        }

        if (request.JourneyId is not { } journeyId || journeyId == Guid.Empty)
        {
            error = "journeyId is required.";
            return false;
        }

        if (request.FromStopId is not { } fromStopId || request.ToStopId is not { } toStopId)
        {
            error = "fromStopId and toStopId are required.";
            return false;
        }

        if (request.ProgressPermille is not { } progress
            || progress is < 0 or > RunningCalculator.FullProgress)
        {
            error = $"progressPermille is required and must be between 0 and {RunningCalculator.FullProgress}.";
            return false;
        }

        if (request.ReportedDelaySeconds is not { } delay || delay < 0)
        {
            error = "reportedDelaySeconds is required and must not be negative.";
            return false;
        }

        if (!InstantText.TryParse(request.ObservedAt, out var observedAt))
        {
            error = "observedAt is required and must be an ISO 8601 timestamp with an offset, such as 2026-10-14T07:00:00+05:30.";
            return false;
        }

        // PostgreSQL keeps microseconds, so store exactly what a retry will compare against.
        var utc = observedAt.ToUniversalTime();
        utc = new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
        if (utc > now)
        {
            error = "observedAt must not be in the future.";
            return false;
        }

        error = "";
        input = new ObservationInput(request.SourceEventId, journeyId, fromStopId, toStopId, progress, delay, utc);
        return true;
    }

    // The two stops must be neighbouring occurrences of this journey's pattern, in order.
    public static bool IsSegmentOf(ObservationInput input, IEnumerable<PatternStop> patternStops) =>
        RunningCalculator.IndexOfSegmentEnd(
            patternStops.OrderBy(s => s.Sequence).ToList(), input.FromStopId, input.ToStopId) >= 0;

    // A stored row is "the same event" when every reported fact matches; receipt time is ours.
    public static bool IsSameEvent(Observation stored, ObservationInput input) =>
        stored.JourneyId == input.JourneyId
        && stored.FromStopId == input.FromStopId
        && stored.ToStopId == input.ToStopId
        && stored.ProgressPermille == input.ProgressPermille
        && stored.ReportedDelaySeconds == input.ReportedDelaySeconds
        && stored.ObservedAt == input.ObservedAt;

    // The evidence a journey is judged on: the greatest observed_at, never the latest receipt.
    // Two reports at the same instant are separated by the greater event ID, compared ordinally.
    public static Observation? Current(IEnumerable<Observation> observations) =>
        observations
            .OrderByDescending(o => o.ObservedAt)
            .ThenByDescending(o => o.SourceEventId, StringComparer.Ordinal)
            .FirstOrDefault();
}
