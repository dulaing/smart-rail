using System.Globalization;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Rail;

namespace SmartRail.Api.Modules.Operations.Briefings;

// Protected reads for a driver's assigned journeys. The driver is always the token's own account:
// no route takes a driver ID, and the assignment and the driver.briefing.read permission are
// rechecked from the database on every request. Unlike the public change read, the briefing
// names who confirmed each change.
public static class DriverBriefingEndpoints
{
    public static void MapDriverBriefings(this IEndpointRouteBuilder app)
    {
        var journeys = app.MapGroup("/api/driver/journeys")
            .RequireAuthorization(AccessPolicies.ActiveAccount);

        journeys.MapGet("", List);
        journeys.MapGet("{journeyId:guid}/briefing", Read);
    }

    // The journeys assigned to the caller on one service date. No assignment is an empty list, not
    // an error; an account without the permission is refused.
    private static async Task<IResult> List(
        string? serviceDate, HttpContext context, IDriverAssignments assignments, IJourneyRunningReader rail,
        IJourneyStopReader stops, ChangeStore store)
    {
        if (!DateOnly.TryParseExact(serviceDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return Results.Problem(
                title: "Invalid serviceDate", detail: "serviceDate is required and must be a date like 2026-10-14.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (await assignments.JourneysAsync(context, Permissions.DriverBriefingRead) is not { } assigned)
        {
            return Results.Forbid();
        }

        var found = await rail.ListAsync(assigned, date, context.RequestAborted);
        var items = new List<DriverJourneyDto>();
        foreach (var journey in found)
        {
            items.Add(DriverJourneyDto.From(journey, await AvailabilityAsync(journey.JourneyId, stops, store, context.RequestAborted)));
        }

        return PrivateJson(context, new DriverJourneysDto(date, items));
    }

    private static async Task<IResult> Read(
        Guid journeyId, HttpContext context, IJourneyStopReader stops, AccessGuard guard,
        IJourneyRunningReader rail, ChangeStore store, IAccountNames names)
    {
        if (await stops.FindAsync(journeyId, context.RequestAborted) is null)
        {
            return Results.Problem(
                title: "Unknown journey", detail: "No journey has this ID.", statusCode: StatusCodes.Status404NotFound);
        }

        // An unassigned driver, a lost grant or a disabled account all end here, audited by the guard.
        if (await guard.CheckAsync(context, Permissions.DriverBriefingRead, new AccessTarget.Journey(journeyId))
            != AccessDecision.Allowed)
        {
            return Results.Forbid();
        }

        var running = await rail.ReadAsync(journeyId, context.RequestAborted);
        if (running is null)
        {
            return Results.Problem(
                title: "Unknown journey", detail: "No journey has this ID.", statusCode: StatusCodes.Status404NotFound);
        }

        var changes = await store.LoadAsync(journeyId, context.RequestAborted);
        var active = changes is null ? [] : ConfirmedState.Fold(changes.History).Active;
        var issuers = await names.FindAsync(active.Select(c => c.PublishedById).Distinct().ToList(), context.RequestAborted);

        // The server clock goes on every 200 and 304, as on the public running read.
        context.Response.Headers[RunningDetail.ServerTimeHeader] = running.ServerTime;
        return PrivateJson(context, DriverBriefingDto.From(running, changes?.Version ?? 0, active, issuers));
    }

    private static async Task<string> AvailabilityAsync(
        Guid journeyId, IJourneyStopReader stops, ChangeStore store, CancellationToken cancellationToken)
    {
        var journey = await stops.FindOperationsJourneyAsync(journeyId, cancellationToken);
        var changes = await store.LoadAsync(journeyId, cancellationToken);
        return journey is null || changes is null
            ? ServiceAvailability.AsScheduled.ToCode()
            : JourneyOutcome.From(journey, ConfirmedState.Fold(changes.History)).Availability.ToCode();
    }

    // A body with an ETag that only the signed-in driver's device may keep.
    private static IResult PrivateJson<T>(HttpContext context, T body)
    {
        var result = ETagResults.JsonWithETag(context, body);
        context.Response.Headers.CacheControl = "private, no-cache";
        return result;
    }
}
