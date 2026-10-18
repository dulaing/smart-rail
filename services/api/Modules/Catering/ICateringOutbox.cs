namespace SmartRail.Api.Modules.Catering;

// When the train reaches one stop, as calculated from the state after the change. The estimate and
// its evidence time are both null while the position is unknown, and for a stop the train no
// longer serves; a null is never replaced by the scheduled time.
public sealed record TrainChangeStop(
    Guid StopId,
    Guid StationId,
    DateTimeOffset ScheduledArrival,
    DateTimeOffset? EstimatedArrival,
    DateTimeOffset? EtaEvidenceAt);

// What RailBites is told about a confirmed change. Every message carries the journey version it
// was made at, the full eligibility snapshot and the pickup times of every stop, never just a
// difference, so the provider can ignore an older message that arrives late, a withdrawal can
// reopen what a change closed, and a delay moves each open order's time.
public sealed record TrainChangeMessage(
    Guid ServiceChangeId,
    Guid JourneyId,
    string TrainNumber,
    DateOnly ServiceDate,
    int JourneyVersion,
    string ChangeKind,
    string Action,
    bool OrderClosed,
    IReadOnlyList<Guid> BlockedPickupStopIds,
    DateTimeOffset CalculatedAt,
    IReadOnlyList<TrainChangeStop> Stops);

// The small door other modules use to hand a message to Catering. It only stages the message in
// the caller's unit of work: it is stored when the caller saves, in the same transaction as the
// change that caused it. Delivery to RailBites happens later and is not done here.
public interface ICateringOutbox
{
    void Enqueue(TrainChangeMessage message);
}
