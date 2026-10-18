namespace SmartRail.Api.Modules.Rail;

// A platform staff have confirmed for one stop occurrence, and when they did.
public sealed record ConfirmedPlatform(string Platform, DateTimeOffset ConfirmedAt);

// The staff decisions in force for a journey, as Rail needs them to calculate running detail.
// Rail does not know how they were made, who made them or how they are stored. The confirmation
// times and the journey version let a public read say when each decision was made and which
// state of the journey it describes; they are null where there is no such decision.
public sealed record ConfirmedOverrides(
    int? DelayMinutes,
    bool Cancelled,
    Guid? FinalStopId,
    IReadOnlyDictionary<Guid, ConfirmedPlatform> Platforms,
    DateTimeOffset? DelayConfirmedAt = null,
    DateTimeOffset? CancelledAt = null,
    DateTimeOffset? EndsEarlyAt = null,
    int? Version = null)
{
    public static readonly ConfirmedOverrides None = new(null, false, null, new Dictionary<Guid, ConfirmedPlatform>());
}

// The small door Rail uses to ask for the confirmed decisions on a journey. Operations supplies
// them; Rail never reads Operations' tables.
public interface IConfirmedChangeReader
{
    Task<ConfirmedOverrides> ReadAsync(Guid journeyId, CancellationToken cancellationToken);
}
