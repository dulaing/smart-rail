using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Access;

// A local account as the rules see it, plus the name to show.
public sealed record LocalAccount(string DisplayName, AccessAccount Access)
{
    public Guid Id => Access.Id;
}

// Reads a local account, its grants and its assignments straight from the database.
public sealed class AccountDirectory(SmartRailDbContext db)
{
    public async Task<LocalAccount?> FindAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Issuer == issuer && a.Subject == subject, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var grants = await db.RoleGrants.AsNoTracking()
            .Where(g => g.AccountId == account.Id)
            .Select(g => new
            {
                g.Id,
                RoleCode = g.Role.Code,
                g.StationScopeId,
                g.ValidUntil,
                Permissions = g.Role.Permissions.Select(p => p.PermissionCode).ToList(),
            })
            .ToListAsync(cancellationToken);

        var assigned = await db.JourneyAssignments.AsNoTracking()
            .Where(a => a.DriverId == account.Id)
            .Select(a => a.JourneyId)
            .ToListAsync(cancellationToken);

        var access = new AccessAccount(
            account.Id,
            account.Status,
            grants.Select(g => new AccessGrant(g.Id, g.RoleCode, g.StationScopeId, g.ValidUntil, g.Permissions.ToHashSet())).ToList(),
            assigned.ToHashSet());
        return new LocalAccount(account.DisplayName, access);
    }
}

// The local account behind the request's validated token. It is read once per request, so the
// authorization handler and the endpoint see the same answer, and never cached across
// requests: disabling an account or removing a grant applies on the next call.
public sealed class CurrentAccount(AccountDirectory directory)
{
    private bool _loaded;
    private LocalAccount? _account;

    public async Task<LocalAccount?> GetAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!_loaded)
        {
            var issuer = user.FindFirstValue("iss");
            var subject = user.FindFirstValue("sub");
            _account = issuer is null || subject is null
                ? null
                : await directory.FindAsync(issuer, subject, cancellationToken);
            _loaded = true;
        }

        return _account;
    }
}

public sealed class ActiveAccountRequirement : IAuthorizationRequirement;

// A valid token is not enough: the subject must map to an enabled local account.
public sealed class ActiveAccountHandler(CurrentAccount current) : AuthorizationHandler<ActiveAccountRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ActiveAccountRequirement requirement)
    {
        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        var account = await current.GetAsync(context.User, cancellationToken);
        if (account?.Access.Status == AccountStatus.Active)
        {
            context.Succeed(requirement);
        }
    }
}
