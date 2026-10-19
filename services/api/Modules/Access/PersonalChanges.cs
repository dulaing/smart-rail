namespace SmartRail.Api.Modules.Access;

// One staff decision in force on a followed journey, as the owner's personal changes need it.
// It carries no confirmer identity, so a passenger response never names a protected issuer.
public sealed record PersonalChange(
    Guid ChangeId,
    string Kind,
    DateTimeOffset ConfirmedAt,
    IReadOnlyList<Guid> AffectedStopIds);

// A followed journey's current change version and the decisions in force at that version.
public sealed record PersonalJourneyChanges(
    Guid JourneyId,
    int JourneyVersion,
    IReadOnlyList<PersonalChange> Changes);

// The named door Access uses to read the changes currently in force for followed journeys.
// Operations builds it from its own append-only history; Access never reads Operations' tables.
public interface IPersonalChangeReader
{
    Task<IReadOnlyList<PersonalJourneyChanges>> ReadAsync(
        IReadOnlyList<Guid> journeyIds, CancellationToken cancellationToken);
}
