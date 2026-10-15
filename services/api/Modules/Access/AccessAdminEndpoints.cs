using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Access;

// SysAdmin-only management of SmartRail accounts: enable or disable one, and give or remove
// role grants. These change SmartRail's own records. They never create or disable a Keycloak
// identity, and each successful change commits together with its audit row.
public static class AccessAdminEndpoints
{
    public const string SetStatusAction = "account.set_status";
    public const string SetGrantAction = "grant.set";
    public const string RemoveGrantAction = "grant.remove";

    public static void MapAccessAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/accounts");

        admin.MapPut("{accountId:guid}/status", SetStatus)
            .RequireAuthorization(AccessPolicies.For(Permissions.AccountsManage));

        var grants = admin.MapGroup("{accountId:guid}/grants")
            .RequireAuthorization(AccessPolicies.For(Permissions.GrantsManage));
        grants.MapGet("", ListGrants);
        grants.MapPost("", SetGrant);
        grants.MapDelete("{grantId:guid}", RemoveGrant);
    }

    private static async Task<IResult> SetStatus(
        Guid accountId, StatusRequest request, HttpContext context,
        SmartRailDbContext db, CurrentAccount current, AccessGuard guard)
    {
        var actor = (await current.GetAsync(context.User, context.RequestAborted))!;
        if (request.Status is not ("ACTIVE" or "DISABLED"))
        {
            return Bad("Invalid status", "status must be ACTIVE or DISABLED.");
        }

        if (accountId == actor.Id)
        {
            return OwnAccount();
        }

        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId, context.RequestAborted);
        if (account is null)
        {
            return NotFound("Unknown account");
        }

        var status = AccountStatuses.FromCode(request.Status);
        if (account.Status != status)
        {
            account.Status = status;
            db.AuditEntries.Add(guard.NewEntry(context, actor.Id, SetStatusAction, AuditOutcomes.Succeeded, "ACCOUNT", accountId));
            await db.SaveChangesAsync(context.RequestAborted);
        }

        return Results.Ok(new AccountStatusDto(account.Id, account.Status.ToCode()));
    }

    private static async Task<IResult> ListGrants(Guid accountId, HttpContext context, SmartRailDbContext db)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId, context.RequestAborted))
        {
            return NotFound("Unknown account");
        }

        var grants = await db.RoleGrants.AsNoTracking()
            .Where(g => g.AccountId == accountId)
            .OrderBy(g => g.Role.Code).ThenBy(g => g.StationScopeId)
            .Select(g => new GrantDto(g.Id, g.Role.Code, g.StationScopeId, g.ValidUntil))
            .ToListAsync(context.RequestAborted);
        return Results.Ok(grants);
    }

    private static async Task<IResult> SetGrant(
        Guid accountId, GrantRequest request, HttpContext context,
        SmartRailDbContext db, CurrentAccount current, AccessGuard guard, TimeProvider time)
    {
        var actor = (await current.GetAsync(context.User, context.RequestAborted))!;
        if (accountId == actor.Id)
        {
            return OwnAccount();
        }

        var role = request.Role is null
            ? null
            : await db.Roles.SingleOrDefaultAsync(r => r.Code == request.Role, context.RequestAborted);
        if (role is null)
        {
            return Bad("Invalid role", "role must be the code of a seeded role.");
        }

        // Only station staff are limited to a station; any other role is pilot-wide.
        var needsStation = role.Code == Roles.StationStaff;
        if (needsStation != request.StationId.HasValue)
        {
            return Bad("Invalid station", needsStation
                ? "STATION_STAFF needs a stationId."
                : "Only STATION_STAFF takes a stationId.");
        }

        if (request.ValidUntil is { } until && until <= time.GetUtcNow())
        {
            return Bad("Invalid validUntil", "validUntil must be in the future.");
        }

        if (!await db.Accounts.AnyAsync(a => a.Id == accountId, context.RequestAborted))
        {
            return NotFound("Unknown account");
        }

        if (request.StationId is { } stationId && !await db.Stations.AnyAsync(s => s.Id == stationId, context.RequestAborted))
        {
            return Bad("Invalid station", "stationId is not a known station.");
        }

        // One grant per role and station: asking again changes its end date.
        var grant = await db.RoleGrants.SingleOrDefaultAsync(
            g => g.AccountId == accountId && g.RoleId == role.Id && g.StationScopeId == request.StationId,
            context.RequestAborted);
        if (grant is null)
        {
            grant = new RoleGrant
            {
                Id = Guid.NewGuid(), AccountId = accountId, RoleId = role.Id,
                StationScopeId = request.StationId, ValidUntil = request.ValidUntil,
            };
            db.RoleGrants.Add(grant);
        }
        else
        {
            grant.ValidUntil = request.ValidUntil;
        }

        db.AuditEntries.Add(guard.NewEntry(context, actor.Id, SetGrantAction, AuditOutcomes.Succeeded, "GRANT", grant.Id));
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.Ok(new GrantDto(grant.Id, role.Code, grant.StationScopeId, grant.ValidUntil));
    }

    private static async Task<IResult> RemoveGrant(
        Guid accountId, Guid grantId, HttpContext context,
        SmartRailDbContext db, CurrentAccount current, AccessGuard guard)
    {
        var actor = (await current.GetAsync(context.User, context.RequestAborted))!;
        if (accountId == actor.Id)
        {
            return OwnAccount();
        }

        var grant = await db.RoleGrants.SingleOrDefaultAsync(
            g => g.Id == grantId && g.AccountId == accountId, context.RequestAborted);
        if (grant is null)
        {
            return NotFound("Unknown grant");
        }

        db.RoleGrants.Remove(grant);
        db.AuditEntries.Add(guard.NewEntry(context, actor.Id, RemoveGrantAction, AuditOutcomes.Succeeded, "GRANT", grantId));
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.NoContent();
    }

    private static IResult Bad(string title, string detail) =>
        Results.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static IResult NotFound(string title) =>
        Results.Problem(title: title, statusCode: StatusCodes.Status404NotFound);

    // A lone administrator must not be able to lock themselves out.
    private static IResult OwnAccount() =>
        Results.Problem(
            title: "Not your own account",
            detail: "Ask another system administrator to change your own status or grants.",
            statusCode: StatusCodes.Status409Conflict);

    private sealed record StatusRequest(string? Status);

    private sealed record GrantRequest(string? Role, Guid? StationId, DateTimeOffset? ValidUntil);

    private sealed record AccountStatusDto(Guid AccountId, string Status);

    private sealed record GrantDto(Guid GrantId, string Role, Guid? StationId, DateTimeOffset? ValidUntil);
}
