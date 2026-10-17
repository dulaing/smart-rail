using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Catering.Outbox;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Operations.Reports;
using SmartRail.Api.Modules.Rail;
using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Infrastructure;

// The pilot's one EF Core context. Each module keeps its own mapping beside its code and
// this class only brings them together.
public sealed class SmartRailDbContext(DbContextOptions<SmartRailDbContext> options) : DbContext(options)
{
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<ServicePattern> ServicePatterns => Set<ServicePattern>();
    public DbSet<PatternStop> PatternStops => Set<PatternStop>();
    public DbSet<ScheduledJourney> Journeys => Set<ScheduledJourney>();
    public DbSet<Observation> Observations => Set<Observation>();

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RoleGrant> RoleGrants => Set<RoleGrant>();
    public DbSet<JourneyAssignment> JourneyAssignments => Set<JourneyAssignment>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<JourneyChangeVersion> JourneyVersions => Set<JourneyChangeVersion>();
    public DbSet<ServiceChange> ServiceChanges => Set<ServiceChange>();
    public DbSet<DriverReport> DriverReports => Set<DriverReport>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyRailModel();
        modelBuilder.ApplyAccessModel();
        modelBuilder.ApplyOperationsModel();
        modelBuilder.ApplyReportsModel();
        modelBuilder.ApplyCateringModel();
    }
}
