using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartRail.Api.Modules.Catering.Provider;

// The shapes Grilled sends and receives (services/grilled/openapi.yaml). They are SmartRail's
// own copies, matched by the contract rather than shared with the Node service.
public sealed record ProviderCollectionPoint(string Id, Guid SmartRailStationId, string Name, int CutoffMinutes);

public sealed record ProviderCollectionPoints(IReadOnlyList<ProviderCollectionPoint> CollectionPoints);

public sealed record ProviderMenuItem(string Id, string Name, int PriceMinorLkr, int StockQuantity);

public sealed record ProviderMenu(string CollectionPointId, string Currency, IReadOnlyList<ProviderMenuItem> Items);

// Grilled' Development-only outage switch, as the provider itself answers it: {"outage": bool}.
public sealed record ProviderOutage(bool Outage);

public sealed record ProviderPickupTime(
    DateTimeOffset ScheduledArrival, DateTimeOffset? EstimatedArrival, DateTimeOffset? EtaEvidenceAt);

public sealed record ProviderOrderItem(string MenuItemId, string Name, int Quantity, int UnitPriceMinorLkr);

public sealed record ProviderOrder(
    string Id,
    Guid OrderLinkId,
    string CollectionPointId,
    Guid JourneyId,
    Guid PickupStopId,
    string Status,
    int StatusSequence,
    string PickupCode,
    IReadOnlyList<ProviderOrderItem> Items,
    int TotalMinorLkr,
    ProviderPickupTime PickupTime,
    string? CancellationReason);

public sealed record JourneySnapshot(
    Guid JourneyId,
    string TrainNumber,
    DateOnly ServiceDate,
    int Version,
    bool OrderClosed,
    IReadOnlyList<Guid> BlockedPickupStopIds);

public sealed record PickupSnapshot(
    Guid StopId,
    Guid StationId,
    DateTimeOffset ScheduledArrival,
    DateTimeOffset? EstimatedArrival,
    DateTimeOffset? EtaEvidenceAt);

public sealed record RequestedItem(string MenuItemId, int Quantity);

public sealed record PlaceOrderBody(
    string CollectionPointId, JourneySnapshot Journey, PickupSnapshot Pickup, IReadOnlyList<RequestedItem> Items);

public sealed record CancelOrderBody(JourneySnapshot Journey, PickupSnapshot Pickup);

public sealed record ProviderError(string Code, string Message);

// Grilled' answer to a delivered train-change message: APPLIED, DUPLICATE or STALE. All three
// mean it was committed.
public sealed record EventReceipt(string Result);

public enum ProviderOutcome
{
    // 200 or 201 with a body.
    Success,

    // The provider understood and refused: 4xx with a stable code. Final for that request.
    Refused,

    // 404 on a lookup by link ID: the provider holds no order for it.
    NotFound,

    // Timeout, connection failure, 5xx, bad credentials or missing configuration. The request
    // may or may not have been processed, so this is never proof of rejection.
    Unknown,
}

public sealed record ProviderResponse<T>(ProviderOutcome Outcome, T? Value, ProviderError? Error, int? HttpStatus)
{
    public static ProviderResponse<T> Success(T value, int status) => new(ProviderOutcome.Success, value, null, status);

    public static ProviderResponse<T> Refused(ProviderError error, int status) => new(ProviderOutcome.Refused, default, error, status);

    public static ProviderResponse<T> NotFound(ProviderError? error) => new(ProviderOutcome.NotFound, default, error, 404);

    public static ProviderResponse<T> Unknown(string code, int? status = null) =>
        new(ProviderOutcome.Unknown, default, new ProviderError(code, code), status);
}

public static class ProviderJson
{
    public const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    // Instants cross the boundary as UTC text with milliseconds, which is the contract's format.
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new InstantConverter());
        options.Converters.Add(new NullableInstantConverter());
        return options;
    }

    public static string Format(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString(InstantFormat, CultureInfo.InvariantCulture);

    private sealed class InstantConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(Format(value));
    }

    private sealed class NullableInstantConverter : JsonConverter<DateTimeOffset?>
    {
        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null
                ? null
                : DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(Format(value.Value));
        }
    }
}
