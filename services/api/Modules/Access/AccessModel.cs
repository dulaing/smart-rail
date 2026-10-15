using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Access;

// A SmartRail account linked to one Keycloak identity by (issuer, subject). It holds no
// password, email or token: Keycloak owns those.
public sealed class Account
{
    public Guid Id { get; init; }
    public required string Issuer { get; set; }
    public required string Subject { get; set; }
    public required string DisplayName { get; set; }
    public AccountStatus Status { get; set; }
}

public sealed class Role
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public List<RolePermission> Permissions { get; init; } = [];
}

public sealed class RolePermission
{
    public Guid RoleId { get; init; }
    public required string PermissionCode { get; init; }
}

// A role given to an account, optionally for one station and optionally until a time.
public sealed class RoleGrant
{
    public Guid Id { get; init; }
    public Guid AccountId { get; init; }
    public Guid RoleId { get; init; }
    public Role Role { get; init; } = null!;
    public Guid? StationScopeId { get; init; }
    public DateTimeOffset? ValidUntil { get; set; }
}

public sealed class JourneyAssignment
{
    public Guid Id { get; init; }
    public Guid DriverId { get; init; }
    public Guid JourneyId { get; init; }
}

// One privileged change or denied staff action. It records who, what, on which target and when.
// It never holds a token, request body, email or phone number.
public sealed class AuditEntry
{
    public Guid Id { get; init; }
    public Guid ActorId { get; init; }
    public required string Action { get; init; }
    public required string Outcome { get; init; }
    public required string TargetType { get; init; }
    public Guid? TargetId { get; init; }
    public required string CorrelationId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}

public static class AuditOutcomes
{
    public const string Succeeded = "SUCCEEDED";
    public const string Denied = "DENIED";
}

// Maps the Access module's tables in the identity schema.
public static class AccessModel
{
    public const string Schema = "identity";

    public static void ApplyAccessModel(this ModelBuilder model)
    {
        model.Entity<Account>(account =>
        {
            account.ToTable("accounts", Schema, table =>
                table.HasCheckConstraint("ck_accounts_status", "status IN ('ACTIVE', 'DISABLED')"));
            account.HasKey(a => a.Id);
            account.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            account.Property(a => a.Issuer).HasColumnName("issuer").HasMaxLength(300);
            account.Property(a => a.Subject).HasColumnName("subject").HasMaxLength(200);
            account.Property(a => a.DisplayName).HasColumnName("display_name").HasMaxLength(100);
            account.Property(a => a.Status).HasColumnName("status").HasMaxLength(16)
                .HasConversion(status => status.ToCode(), code => AccountStatuses.FromCode(code));
            account.HasIndex(a => new { a.Issuer, a.Subject }).IsUnique();
        });

        model.Entity<Role>(role =>
        {
            role.ToTable("roles", Schema);
            role.HasKey(r => r.Id);
            role.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            role.Property(r => r.Code).HasColumnName("code").HasMaxLength(32);
            role.HasIndex(r => r.Code).IsUnique();
            role.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<RolePermission>(permission =>
        {
            permission.ToTable("role_permissions", Schema);
            permission.HasKey(p => new { p.RoleId, p.PermissionCode });
            permission.Property(p => p.RoleId).HasColumnName("role_id");
            permission.Property(p => p.PermissionCode).HasColumnName("permission_code").HasMaxLength(64);
        });

        model.Entity<RoleGrant>(grant =>
        {
            grant.ToTable("role_grants", Schema);
            grant.HasKey(g => g.Id);
            grant.Property(g => g.Id).HasColumnName("id").ValueGeneratedNever();
            grant.Property(g => g.AccountId).HasColumnName("account_id");
            grant.Property(g => g.RoleId).HasColumnName("role_id");
            grant.Property(g => g.StationScopeId).HasColumnName("station_scope_id");
            grant.Property(g => g.ValidUntil).HasColumnName("valid_until");
            // One row per role and station, so granting twice is an update, not a second row.
            grant.HasIndex(g => new { g.AccountId, g.RoleId, g.StationScopeId }).IsUnique().AreNullsDistinct(false);
            grant.HasOne<Account>().WithMany().HasForeignKey(g => g.AccountId).OnDelete(DeleteBehavior.Restrict);
            grant.HasOne(g => g.Role).WithMany().HasForeignKey(g => g.RoleId).OnDelete(DeleteBehavior.Restrict);
            grant.HasOne<Station>().WithMany().HasForeignKey(g => g.StationScopeId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<JourneyAssignment>(assignment =>
        {
            assignment.ToTable("journey_assignments", Schema);
            assignment.HasKey(a => a.Id);
            assignment.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            assignment.Property(a => a.DriverId).HasColumnName("driver_id");
            assignment.Property(a => a.JourneyId).HasColumnName("journey_id");
            assignment.HasIndex(a => new { a.DriverId, a.JourneyId }).IsUnique();
            assignment.HasOne<Account>().WithMany().HasForeignKey(a => a.DriverId).OnDelete(DeleteBehavior.Restrict);
            assignment.HasOne<ScheduledJourney>().WithMany().HasForeignKey(a => a.JourneyId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<AuditEntry>(entry =>
        {
            entry.ToTable("audit_entries", Schema, table =>
                table.HasCheckConstraint("ck_audit_entries_outcome", "outcome IN ('SUCCEEDED', 'DENIED')"));
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            entry.Property(e => e.ActorId).HasColumnName("actor_id");
            entry.Property(e => e.Action).HasColumnName("action").HasMaxLength(64);
            entry.Property(e => e.Outcome).HasColumnName("outcome").HasMaxLength(16);
            entry.Property(e => e.TargetType).HasColumnName("target_type").HasMaxLength(32);
            entry.Property(e => e.TargetId).HasColumnName("target_id");
            entry.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasMaxLength(64);
            entry.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            entry.HasIndex(e => e.OccurredAt);
            entry.HasOne<Account>().WithMany().HasForeignKey(e => e.ActorId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
