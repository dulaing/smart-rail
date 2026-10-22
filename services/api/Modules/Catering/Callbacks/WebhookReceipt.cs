namespace SmartRail.Api.Modules.Catering.Callbacks;

// One status callback from Grilled that SmartRail has accepted, in catering.webhook_receipts. It is
// written in the same transaction as the update it caused, so a receipt always means the update was
// decided. The event ID is the primary key: a second delivery of the same event finds it here.
public sealed class WebhookReceipt
{
    public required string ProviderEventId { get; init; }
    public Guid OrderLinkId { get; init; }
    public int StatusSequence { get; init; }

    // Hash of the raw body, so the same event ID carrying different content can be told from a repeat.
    public required string ContentDigest { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
}
