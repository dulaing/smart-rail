using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Infrastructure;

public static class DatabaseSetup
{
    /// <summary>
    /// Registers the database when <c>ConnectionStrings:smartrail</c> is set. Without it (a plain
    /// <c>dotnet run</c>) the API still starts, but the timetable routes are not mapped.
    /// </summary>
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

    /// <summary>
    /// Runs when the API is started with <c>--migrate-and-seed</c>. Migrations are applied by
    /// this explicit command, never silently at ordinary startup. Safe to run repeatedly.
    /// </summary>
    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartRailDbContext>();
        await db.Database.MigrateAsync();

        var start = app.Configuration.GetValue<DateOnly?>("Seed:StartDate")
            ?? ColomboTime.ServiceDateOf(TimeProvider.System.GetUtcNow());
        var days = app.Configuration.GetValue("Seed:Days", 7);
        await DemoTimetableSeeder.SeedAsync(db, start, days);
    }
}
