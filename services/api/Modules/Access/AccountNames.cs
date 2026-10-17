using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Access;

// The small door other modules use to show who someone is, so they never query Access's
// account table. Only the display name leaves Access: no email, subject or status.
public interface IAccountNames
{
    // The display name of each known account. An ID with no account is left out.
    Task<IReadOnlyDictionary<Guid, string>> FindAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);
}

public sealed class AccountNames(SmartRailDbContext db) : IAccountNames
{
    public async Task<IReadOnlyDictionary<Guid, string>> FindAsync(
        IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await db.Accounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.DisplayName, cancellationToken);
    }
}
