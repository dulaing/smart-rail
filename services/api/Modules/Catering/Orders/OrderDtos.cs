using System.Text.Json;
using SmartRail.Api.Modules.Catering.Provider;

namespace SmartRail.Api.Modules.Catering.Orders;

public sealed record OrderItemDto(string MenuItemId, string Name, int Quantity, int UnitPriceMinorLkr);

public sealed record OrderPickupTimeDto(
    DateTimeOffset? ScheduledArrival, DateTimeOffset? EstimatedArrival, DateTimeOffset? EtaEvidenceAt);

// A passenger's view of their own order. Status is PLACING while the provider's answer is unknown,
// REJECTED if it was refused, and otherwise the provider's status. ReasonCode is the stable code
// for a refusal or a cancellation; clients translate it. The pickup code is only ever in this view.
public sealed record OrderDto(
    Guid Id,
    string Status,
    int StatusSequence,
    string CollectionPointId,
    Guid JourneyId,
    Guid PickupStopId,
    string? PickupCode,
    IReadOnlyList<OrderItemDto>? Items,
    int? TotalMinorLkr,
    OrderPickupTimeDto? PickupTime,
    string? ReasonCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public const string Placing = "PLACING";
    public const string Rejected = "REJECTED";

    public static string StatusOf(OrderLink link) => link.PlacementState switch
    {
        PlacementStates.Confirmed => link.ProviderStatus!,
        PlacementStates.Rejected => Rejected,
        _ => Placing,
    };

    public static OrderDto From(OrderLink link)
    {
        var confirmed = link.PlacementState == PlacementStates.Confirmed;
        var items = confirmed && link.ItemsJson is not null
            ? JsonSerializer.Deserialize<List<ProviderOrderItem>>(link.ItemsJson, ProviderJson.Options)!
                .Select(i => new OrderItemDto(i.MenuItemId, i.Name, i.Quantity, i.UnitPriceMinorLkr)).ToList()
            : null;

        return new OrderDto(
            link.Id,
            StatusOf(link),
            link.StatusSequence,
            link.CollectionPointId,
            link.JourneyId,
            link.PickupStopId,
            confirmed ? link.PickupCode : null,
            items,
            items?.Sum(i => i.Quantity * i.UnitPriceMinorLkr),
            confirmed ? new OrderPickupTimeDto(link.ScheduledArrival, link.EstimatedArrival, link.EtaEvidenceAt) : null,
            link.PlacementState == PlacementStates.Rejected ? link.RejectionCode : link.CancellationReason,
            link.CreatedAt,
            link.UpdatedAt);
    }
}

public sealed record OrderPageDto(IReadOnlyList<OrderDto> Orders);

// The body of POST /api/food/orders. Everything about the train comes from SmartRail, not here.
public sealed record CreateOrderRequest(
    string? ClientRequestKey,
    string? CollectionPointId,
    Guid? JourneyId,
    Guid? PickupStopId,
    List<CreateOrderItem>? Items);

public sealed record CreateOrderItem(string? MenuItemId, int? Quantity);
