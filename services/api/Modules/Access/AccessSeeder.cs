using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Modules.Access;

public static class AccessSeeder
{
    /// <summary>
    /// Makes <c>identity.roles</c> and <c>identity.role_permissions</c> match <see cref="RoleMatrix"/>.
    /// Safe to run repeatedly; a permission removed from the matrix is removed here too.
    /// </summary>
    public static async Task SeedRolesAsync(SmartRailDbContext db, CancellationToken cancellationToken = default)
    {
        var existing = await db.Roles.Include(r => r.Permissions).ToDictionaryAsync(r => r.Code, cancellationToken);

        foreach (var code in Roles.All)
        {
            if (!existing.TryGetValue(code, out var role))
            {
                role = new Role { Id = StableIds.For("role", code), Code = code };
                db.Roles.Add(role);
            }

            var wanted = RoleMatrix.PermissionsOf(code).ToHashSet();
            role.Permissions.RemoveAll(p => !wanted.Contains(p.PermissionCode));
            foreach (var permission in wanted.Where(w => role.Permissions.All(p => p.PermissionCode != w)))
            {
                role.Permissions.Add(new RolePermission { RoleId = role.Id, PermissionCode = permission });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
