using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Catering.Provider;

namespace SmartRail.Api.Modules.Catering.Orders;

// A passenger's own food orders. A signed-in, enabled account is required, then orders.manage for
// the caller's own account is rechecked from the database on every request. The account is always
// the token's own; another account's order is reported as not found, never as forbidden, so its
// existence and pickup code are not revealed.
public static class OrderEndpoints
{
    public const string Path = "/api/food/orders";

    private const int MaxItems = 20;
    private const int MaxQuantity = 20;

    public static void MapOrders(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup(Path).RequireAuthorization(AccessPolicies.ActiveAccount);
        orders.MapPost("", Create);
        orders.MapGet("", ListOwn);
        orders.MapGet("{orderId:guid}", ReadOwn);
        orders.MapPost("{orderId:guid}/cancellation", Cancel);
    }

    // 201 when Grilled accepted it, 202 while the outcome is unknown (poll the Location), 200
    // for a replay, 422 when it was refused. A refusal that happens after the link was stored
    // still carries the link ID so the same request key keeps giving the same answer.
    private static async Task<IResult> Create(
        CreateOrderRequest? request, HttpContext context, AccessGuard guard, CurrentAccount current, OrderCreator creator)
    {
        if (await RefuseAsync(context, guard, current) is { } refused)
        {
            return refused;
        }

        if (Validate(request) is { } invalid)
        {
            return Bad(invalid);
        }

        var account = (await current.GetAsync(context.User, context.RequestAborted))!;
        var items = request!.Items!.Select(i => new RequestedItem(i.MenuItemId!.Trim(), i.Quantity!.Value)).ToList();
        var result = await creator.CreateAsync(
            account.Id, request.ClientRequestKey!.Trim(), request.CollectionPointId!.Trim(),
            request.JourneyId!.Value, request.PickupStopId!.Value, items, context.RequestAborted);

        switch (result)
        {
            case CreateResult.Created created:
                return Respond(created.Link, isNew: true);
            case CreateResult.Replayed replayed:
                return Respond(replayed.Link, isNew: false);
            case CreateResult.Unavailable unavailable:
                return Problem(
                    unavailable.Problem == OrderProblems.JourneyNotFound ? StatusCodes.Status404NotFound : StatusCodes.Status422UnprocessableEntity,
                    "Order not possible", "This pickup is not available for ordering.", unavailable.Problem);
            default:
                return Problem(
                    StatusCodes.Status409Conflict, "Request key already used",
                    "This clientRequestKey belongs to a different order. Use a new one.", "CLIENT_REQUEST_KEY_IN_USE");
        }
    }

    private static IResult Respond(OrderLink link, bool isNew)
    {
        var location = $"{Path}/{link.Id}";
        var dto = OrderDto.From(link);
        return link.PlacementState switch
        {
            PlacementStates.Confirmed => isNew ? Results.Created(location, dto) : Results.Ok(dto),
            PlacementStates.Rejected => Results.Json(
                new { type = "about:blank", title = "Order refused", status = 422, code = link.RejectionCode, orderId = link.Id },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Accepted(location, dto),
        };
    }

    private static async Task<IResult> ListOwn(
        int? limit, HttpContext context, AccessGuard guard, CurrentAccount current, SmartRailDbContext db)
    {
        if (await RefuseAsync(context, guard, current) is { } refused)
        {
            return refused;
        }

        var account = (await current.GetAsync(context.User, context.RequestAborted))!;
        var size = Math.Clamp(limit ?? 50, 1, 100);
        var links = await db.OrderLinks.AsNoTracking()
            .Where(l => l.AccountId == account.Id)
            .OrderByDescending(l => l.CreatedAt).ThenBy(l => l.Id)
            .Take(size)
            .ToListAsync(context.RequestAborted);
        return Results.Ok(new OrderPageDto(links.Select(OrderDto.From).ToList()));
    }

    private static async Task<IResult> ReadOwn(
        Guid orderId, HttpContext context, AccessGuard guard, CurrentAccount current, SmartRailDbContext db)
    {
        if (await RefuseAsync(context, guard, current) is { } refused)
        {
            return refused;
        }

        var account = (await current.GetAsync(context.User, context.RequestAborted))!;
        var link = await db.OrderLinks.AsNoTracking()
            .SingleOrDefaultAsync(l => l.Id == orderId && l.AccountId == account.Id, context.RequestAborted);
        return link is null ? NotFound() : Results.Ok(OrderDto.From(link));
    }

    private static async Task<IResult> Cancel(
        Guid orderId, HttpContext context, AccessGuard guard, CurrentAccount current, OrderCanceller canceller)
    {
        if (await RefuseAsync(context, guard, current) is { } refused)
        {
            return refused;
        }

        var account = (await current.GetAsync(context.User, context.RequestAborted))!;
        return await canceller.CancelAsync(account.Id, orderId, context.RequestAborted) switch
        {
            CancelResult.Cancelled cancelled => Results.Ok(OrderDto.From(cancelled.Link)),
            CancelResult.Refused refusal => Problem(StatusCodes.Status409Conflict, "Cannot cancel", refusal.Detail, refusal.Code),
            CancelResult.ProviderUnavailable => Problem(
                StatusCodes.Status503ServiceUnavailable, "Food service unavailable",
                "The food provider could not be reached. Nothing was changed; try again.", "PROVIDER_UNAVAILABLE"),
            _ => NotFound(),
        };
    }

    // orders.manage is an own-account permission: the target is always the caller's own account.
    private static async Task<IResult?> RefuseAsync(HttpContext context, AccessGuard guard, CurrentAccount current)
    {
        var account = await current.GetAsync(context.User, context.RequestAborted);
        if (account is null)
        {
            return Results.Forbid();
        }

        return await guard.CheckAsync(context, Permissions.OrdersManage, new AccessTarget.Own(account.Id)) == AccessDecision.Allowed
            ? null
            : Results.Forbid();
    }

    private static string? Validate(CreateOrderRequest? request)
    {
        if (string.IsNullOrWhiteSpace(request?.ClientRequestKey) || request.ClientRequestKey.Trim().Length > 100)
        {
            return "clientRequestKey is required (at most 100 characters).";
        }

        if (string.IsNullOrWhiteSpace(request.CollectionPointId) || request.CollectionPointId.Trim().Length > 100)
        {
            return "collectionPointId is required.";
        }

        if (request.JourneyId is null || request.JourneyId == Guid.Empty || request.PickupStopId is null || request.PickupStopId == Guid.Empty)
        {
            return "journeyId and pickupStopId are required.";
        }

        if (request.Items is not { Count: > 0 and <= MaxItems })
        {
            return $"items must contain 1 to {MaxItems} entries.";
        }

        if (request.Items.Any(i => string.IsNullOrWhiteSpace(i.MenuItemId) || i.Quantity is not (>= 1 and <= MaxQuantity)))
        {
            return $"Each item needs a menuItemId and a quantity from 1 to {MaxQuantity}.";
        }

        return request.Items.Select(i => i.MenuItemId!.Trim()).Distinct().Count() != request.Items.Count
            ? "Each menu item may appear once; combine the quantities."
            : null;
    }

    private static IResult NotFound() =>
        Problem(StatusCodes.Status404NotFound, "Unknown order", "No order of yours has this ID.", "ORDER_NOT_FOUND");

    private static IResult Bad(string detail) =>
        Problem(StatusCodes.Status400BadRequest, "Invalid order request", detail, "INVALID_REQUEST");

    private static IResult Problem(int status, string title, string detail, string code) =>
        Results.Problem(title: title, detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
