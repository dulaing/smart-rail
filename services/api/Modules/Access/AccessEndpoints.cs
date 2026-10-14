namespace SmartRail.Api.Modules.Access;

/// <summary>Endpoints about the signed-in caller's own local account.</summary>
public static class AccessEndpoints
{
    public static void MapAccessEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/me", GetMe).RequireAuthorization(AccessPolicies.ActiveAccount);
    }

    private static async Task<IResult> GetMe(HttpContext context, CurrentAccount current, TimeProvider time)
    {
        // The authorization policy already loaded and checked the account for this request.
        var account = await current.GetAsync(context.User, context.RequestAborted);
        if (account is null)
        {
            return Results.Forbid();
        }

        var now = time.GetUtcNow();
        return Results.Ok(new MeResponse(
            account.Id,
            account.DisplayName,
            account.Access.Status.ToCode(),
            account.Access.Grants.Where(g => g.IsActiveAt(now))
                .OrderBy(g => g.RoleCode, StringComparer.Ordinal).ThenBy(g => g.StationScopeId)
                .Select(g => new GrantDto(g.RoleCode, g.StationScopeId, g.ValidUntil)).ToList(),
            EffectivePermissions.Of(account.Access, now)
                .Select(p => new PermissionDto(p.Code, p.Scope.ToString().ToUpperInvariant(), p.StationIds, p.JourneyIds))
                .ToList()));
    }

    private sealed record GrantDto(string Role, Guid? StationId, DateTimeOffset? ValidUntil);

    private sealed record PermissionDto(string Code, string Scope, IReadOnlyList<Guid> StationIds, IReadOnlyList<Guid> JourneyIds);

    private sealed record MeResponse(
        Guid AccountId, string DisplayName, string Status,
        IReadOnlyList<GrantDto> Grants, IReadOnlyList<PermissionDto> Permissions);
}
