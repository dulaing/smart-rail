using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Rail;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Infrastructure;

/// <summary>
/// The pilot's one EF Core context. Each module keeps its own mapping beside its code and
/// this class only brings them together.
/// </summary>
public sealed class SmartRailDbContext(DbContextOptions<SmartRailDbContext> options) : DbContext(options)
{
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<ServicePattern> ServicePatterns => Set<ServicePattern>();
    public DbSet<PatternStop> PatternStops => Set<PatternStop>();
    public DbSet<ScheduledJourney> Journeys => Set<ScheduledJourney>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyRailModel();
    }
}
