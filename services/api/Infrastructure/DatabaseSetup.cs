using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Rail;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Infrastructure;

public static class DatabaseSetup
{
    // Registers the database when ConnectionStrings:smartrail is set. Without it (a plain
    // dotnet run) the API still starts, but the timetable routes are not mapped.
    public static bool AddSmartRailDatabase(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("smartrail");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        builder.Services.AddDbContext<SmartRailDbContext>(options => options.UseNpgsql(connectionString));
        return true;
    }

    // Runs when the API is started with --migrate-and-seed. Migrations are applied by
    // this explicit command, never silently at ordinary startup. Safe to run repeatedly.
    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartRailDbContext>();
        await db.Database.MigrateAsync();

        var start = app.Configuration.GetValue<DateOnly?>("Seed:StartDate")
            ?? ColomboTime.ServiceDateOf(TimeProvider.System.GetUtcNow());
        var days = app.Configuration.GetValue("Seed:Days", 7);
        await SeedAsync(db, start, days);
    }

    // The seed data every database needs: the role matrix, the demo timetable and a change
    // version row for every journey.
    public static async Task SeedAsync(SmartRailDbContext db, DateOnly startDate, int days)
    {
        await AccessSeeder.SeedRolesAsync(db);
        await DemoTimetableSeeder.SeedAsync(db, startDate, days);
        await OperationsSeeder.SeedVersionsAsync(db, new JourneyStopReader(db), TimeProvider.System.GetUtcNow());
    }
}
