namespace SmartRail.Api.Modules.Catering;

// What RailBites is told about a confirmed change. Every message carries the journey version it
// was made at and the full eligibility snapshot, never just a difference, so the provider can
// ignore an older message that arrives late and a withdrawal can reopen what a change closed.
public sealed record TrainChangeMessage(
    Guid ServiceChangeId,
    Guid JourneyId,
    int JourneyVersion,
    string ChangeKind,
    string Action,
    int? ConfirmedDelayMinutes,
    bool OrderClosed,
    IReadOnlyList<Guid> BlockedPickupStopIds);

// The small door other modules use to hand a message to Catering. It only stages the message in
// the caller's unit of work: it is stored when the caller saves, in the same transaction as the
// change that caused it. Delivery to RailBites happens later and is not done here.
public interface ICateringOutbox
{
    void Enqueue(TrainChangeMessage message);
}
