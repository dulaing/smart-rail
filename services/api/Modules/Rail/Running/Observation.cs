namespace SmartRail.Api.Modules.Rail.Running;

// One stored report of where a simulated train was, in live.observations. Reports are
// history: a later arrival never overwrites an earlier one. Estimates and freshness are
// calculated from these rows and never stored.
public sealed class Observation
{
    public Guid Id { get; init; }
    public Guid JourneyId { get; init; }
    public required string SourceEventId { get; init; }
    public Guid FromStopId { get; init; }
    public Guid ToStopId { get; init; }
    public int ProgressPermille { get; init; }
    public int ReportedDelaySeconds { get; init; }

    // When the train was observed, as reported. This drives freshness and which row is current.
    public DateTimeOffset ObservedAt { get; init; }

    // When the server stored it. Never used to decide age or which row is current.
    public DateTimeOffset ReceivedAt { get; init; }

    public RunningObservation ToRunning() =>
        new(FromStopId, ToStopId, ProgressPermille, ReportedDelaySeconds, ObservedAt);
}
