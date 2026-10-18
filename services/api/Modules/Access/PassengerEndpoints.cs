using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Access;

// The signed-in passenger's own saved commutes and followed dated journeys. The owner is always
// the active local account from the token: no route takes an account from the request. A save is
// a reusable origin/destination pair; a follow is one dated journey, not every train with its
// number. Every call rechecks the matching Own permission from the database, so losing a grant
// takes effect on the next call.
public static class PassengerEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    public static void MapPassengerEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/me").RequireAuthorization(AccessPolicies.ActiveAccount);

        me.MapGet("/saved-routes", ListSavedRoutes);
        me.MapPost("/saved-routes", SaveRoute);
        me.MapDelete("/saved-routes/{routeId:guid}", DeleteSavedRoute);

        me.MapGet("/followed-journeys", ListFollows);
        me.MapPut("/followed-journeys/{journeyId:guid}", Follow);
        me.MapDelete("/followed-journeys/{journeyId:guid}", Unfollow);
    }

    private static async Task<IResult> ListSavedRoutes(
        int? limit, HttpContext context, SmartRailDbContext db, CurrentAccount current, AccessGuard guard)
    {
        if (await PermitAsync(context, Permissions.SavedRoutesManage, current, guard) is not { } account)
        {
            return Results.Forbid();
        }

        var routes = await db.SavedRoutes.AsNoTracking()
            .Where(r => r.AccountId == account.Id)
            .OrderBy(r => r.OriginStationId).ThenBy(r => r.DestinationStationId).ThenBy(r => r.Id)
            .Take(SizeFor(limit))
            .Select(r => new SavedRouteDto(r.Id, r.OriginStationId, r.DestinationStationId))
            .ToListAsync(context.RequestAborted);
        return ETagResults.JsonWithETag(context, routes);
    }

    // 201 for a new route, 200 for a repeat of the same pair. A race stores one row, not two.
    private static async Task<IResult> SaveRoute(
        SavedRouteRequest? request, HttpContext context, SmartRailDbContext db,
        CurrentAccount current, AccessGuard guard, IRailReferenceReader rails)
    {
        if (await PermitAsync(context, Permissions.SavedRoutesManage, current, guard) is not { } account)
        {
            return Results.Forbid();
        }

        if (request?.OriginStationId is not { } origin || request.DestinationStationId is not { } destination)
        {
            return Bad("A saved route needs originStationId and destinationStationId.");
        }

        if (origin == destination)
        {
            return Bad("originStationId and destinationStationId must be different stations.");
        }

        if (!await rails.StationExistsAsync(origin, context.RequestAborted)
            || !await rails.StationExistsAsync(destination, context.RequestAborted))
        {
            return Bad("originStationId or destinationStationId is not a known station.");
        }

        if (await FindRouteAsync(db, account.Id, origin, destination, context.RequestAborted) is { } existing)
        {
            return Results.Ok(SavedRouteDto.From(existing));
        }

        var route = new SavedRoute
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            OriginStationId = origin,
            DestinationStationId = destination,
        };
        db.SavedRoutes.Add(route);
        try
        {
            await db.SaveChangesAsync(context.RequestAborted);
        }
        catch (DbUpdateException)
        {
            // Another request won the race; the unique index kept one row. Return the winner.
            db.Entry(route).State = EntityState.Detached;
            if (await FindRouteAsync(db, account.Id, origin, destination, context.RequestAborted) is { } raced)
            {
                return Results.Ok(SavedRouteDto.From(raced));
            }

            throw;
        }

        return Results.Created($"/api/me/saved-routes/{route.Id}", SavedRouteDto.From(route));
    }

    private static async Task<IResult> DeleteSavedRoute(
        Guid routeId, HttpContext context, SmartRailDbContext db, CurrentAccount current, AccessGuard guard)
    {
        if (await PermitAsync(context, Permissions.SavedRoutesManage, current, guard) is not { } account)
        {
            return Results.Forbid();
        }

        // Filtering by owner makes another passenger's route invisible, so it is a 404 here.
        var route = await db.SavedRoutes.SingleOrDefaultAsync(
            r => r.Id == routeId && r.AccountId == account.Id, context.RequestAborted);
        if (route is null)
        {
            return NotFound("Unknown saved route");
        }

        db.SavedRoutes.Remove(route);
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> ListFollows(
        int? limit, HttpContext context, SmartRailDbContext db, CurrentAccount current, AccessGuard guard)
    {
        if (await PermitAsync(context, Permissions.FollowsManage, current, guard) is not { } account)
        {
            return Results.Forbid();
        }

        var follows = await db.FollowedJourneys.AsNoTracking()
            .Where(f => f.AccountId == account.Id)
            .OrderByDescending(f => f.FollowedAt).ThenBy(f => f.JourneyId)
            .Take(SizeFor(limit))
            .Select(f => new FollowedJourneyDto(f.JourneyId, f.FollowedAt))
            .ToListAsync(context.RequestAborted);
        return ETagResults.JsonWithETag(context, follows);
    }

    // 201 for a new follow, 200 for a repeat with the original followedAt. A race stores one row.
    private static async Task<IResult> Follow(
        Guid journeyId, HttpContext context, SmartRailDbContext db, CurrentAccount current,
        AccessGuard guard, IRailReferenceReader rails, TimeProvider time)
    {
        if (await PermitAsync(context, Permissions.FollowsManage, current, guard) is not { } account)
        {
            return Results.Forbid();
        }

        if (!await rails.JourneyExistsAsync(journeyId, context.RequestAborted))
        {
            return Bad("journeyId is not a known dated journey.");
        }

        if (await FindFollowAsync(db, account.Id, journeyId, context.RequestAborted) is { } existing)
        {
            return Results.Ok(FollowedJourneyDto.From(existing));
        }

        var follow = new FollowedJourney
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            JourneyId = journeyId,
            FollowedAt = time.GetUtcNow(),
        };
        db.FollowedJourneys.Add(follow);
        try
        {
            await db.SaveChangesAsync(context.RequestAborted);
        }
        catch (DbUpdateException)
        {
            db.Entry(follow).State = EntityState.Detached;
            if (await FindFollowAsync(db, account.Id, journeyId, context.RequestAborted) is { } raced)
            {
                return Results.Ok(FollowedJourneyDto.From(raced));
            }

            throw;
        }

        return Results.Created($"/api/me/followed-journeys/{journeyId}", FollowedJourneyDto.From(follow));
    }

    // Unfollowing is idempotent: removing a follow that is already gone is still a success, so a
    // retry cannot fail and cannot touch another account's row.
    private static async Task<IResult> Unfollow(
        Guid journeyId, HttpContext context, SmartRailDbContext db, CurrentAccount current, AccessGuard guard)
    {
        if (await PermitAsync(context, Permissions.FollowsManage, current, guard) is not { } account)
        {
            return Results.Forbid();
        }

        var follow = await db.FollowedJourneys.SingleOrDefaultAsync(
            f => f.AccountId == account.Id && f.JourneyId == journeyId, context.RequestAborted);
        if (follow is not null)
        {
            db.FollowedJourneys.Remove(follow);
            await db.SaveChangesAsync(context.RequestAborted);
        }

        return Results.NoContent();
    }

    // The active local account when it holds the Own permission, otherwise null so the caller
    // forbids. A refusal is audited by the guard against the caller's own account.
    private static async Task<LocalAccount?> PermitAsync(
        HttpContext context, string permission, CurrentAccount current, AccessGuard guard)
    {
        var account = await current.GetAsync(context.User, context.RequestAborted);
        if (account is null)
        {
            return null;
        }

        return await guard.CheckAsync(context, permission, new AccessTarget.Own(account.Id)) == AccessDecision.Allowed
            ? account
            : null;
    }

    private static Task<SavedRoute?> FindRouteAsync(
        SmartRailDbContext db, Guid accountId, Guid origin, Guid destination, CancellationToken cancellationToken) =>
        db.SavedRoutes.AsNoTracking().SingleOrDefaultAsync(
            r => r.AccountId == accountId && r.OriginStationId == origin && r.DestinationStationId == destination,
            cancellationToken);

    private static Task<FollowedJourney?> FindFollowAsync(
        SmartRailDbContext db, Guid accountId, Guid journeyId, CancellationToken cancellationToken) =>
        db.FollowedJourneys.AsNoTracking().SingleOrDefaultAsync(
            f => f.AccountId == accountId && f.JourneyId == journeyId, cancellationToken);

    private static int SizeFor(int? requested) => Math.Clamp(requested ?? DefaultPageSize, 1, MaxPageSize);

    private static IResult Bad(string detail) =>
        Results.Problem(title: "Invalid passenger request", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static IResult NotFound(string title) =>
        Results.Problem(title: title, statusCode: StatusCodes.Status404NotFound);

    private sealed record SavedRouteRequest(Guid? OriginStationId, Guid? DestinationStationId);

    private sealed record SavedRouteDto(Guid Id, Guid OriginStationId, Guid DestinationStationId)
    {
        public static SavedRouteDto From(SavedRoute route) =>
            new(route.Id, route.OriginStationId, route.DestinationStationId);
    }

    private sealed record FollowedJourneyDto(Guid JourneyId, DateTimeOffset FollowedAt)
    {
        public static FollowedJourneyDto From(FollowedJourney follow) =>
            new(follow.JourneyId, follow.FollowedAt);
    }
}
