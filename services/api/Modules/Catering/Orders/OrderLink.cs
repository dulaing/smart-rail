namespace SmartRail.Api.Modules.Catering.Orders;

public static class PlacementStates
{
    public const string Pending = "PENDING";
    public const string Confirmed = "CONFIRMED";
    public const string Rejected = "REJECTED";
}

// The link between one passenger's request and the provider's order, in catering.order_links.
// SmartRail's own id for it (Id) is what Grilled sees; the passenger's request key never leaves
// SmartRail. Placement uncertainty lives here: a link stays PENDING while the provider's answer is
// unknown, and only a provider answer (or a safe reconciliation) makes it CONFIRMED or REJECTED.
public sealed class OrderLink
{
    public Guid Id { get; init; }
    public Guid AccountId { get; init; }
    public Guid JourneyId { get; init; }
    public Guid PickupStopId { get; init; }
    public required string ClientRequestKey { get; init; }
    public required string CollectionPointId { get; init; }

    // Hash of what was asked for (collection point, journey, stop, items). It outlives the request
    // JSON so a later reuse of the key with different input can still be refused.
    public required string RequestDigest { get; init; }

    // The collection point, journey, stop and items as the passenger asked, kept only while the
    // outcome is unknown so a timed-out placement can be resent unchanged.
    public string? RequestPayload { get; set; }

    public required string PlacementState { get; set; }
    public string? RejectionCode { get; set; }

    public string? ProviderOrderRef { get; set; }
    public string? ProviderStatus { get; set; }
    public int StatusSequence { get; set; }
    public string? CancellationReason { get; set; }
    public string? PickupCode { get; set; }
    public string? ItemsJson { get; set; }
    public DateTimeOffset? ScheduledArrival { get; set; }
    public DateTimeOffset? EstimatedArrival { get; set; }
    public DateTimeOffset? EtaEvidenceAt { get; set; }

    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}
