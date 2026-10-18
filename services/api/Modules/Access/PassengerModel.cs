using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Access;

// A reusable origin/destination pair an account saved. Saving a commute never subscribes to a
// train; only a follow does.
public sealed class SavedRoute
{
    public Guid Id { get; init; }
    public Guid AccountId { get; init; }
    public Guid OriginStationId { get; init; }
    public Guid DestinationStationId { get; init; }
}

// One dated journey an account follows. FollowedAt is set by the server, never the client.
public sealed class FollowedJourney
{
    public Guid Id { get; init; }
    public Guid AccountId { get; init; }
    public Guid JourneyId { get; init; }
    public DateTimeOffset FollowedAt { get; init; }
}

// Maps the Access module's passenger-owned tables in the passenger schema.
public static class PassengerModel
{
    public const string Schema = "passenger";

    public static void ApplyPassengerModel(this ModelBuilder model)
    {
        model.Entity<SavedRoute>(route =>
        {
            route.ToTable("saved_routes", Schema);
            route.HasKey(r => r.Id);
            route.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            route.Property(r => r.AccountId).HasColumnName("account_id");
            route.Property(r => r.OriginStationId).HasColumnName("origin_station_id");
            route.Property(r => r.DestinationStationId).HasColumnName("destination_station_id");
            // The database, not the application, makes a duplicate save impossible.
            route.HasIndex(r => new { r.AccountId, r.OriginStationId, r.DestinationStationId }).IsUnique();
            route.HasOne<Account>().WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Restrict);
            route.HasOne<Station>().WithMany().HasForeignKey(r => r.OriginStationId).OnDelete(DeleteBehavior.Restrict);
            route.HasOne<Station>().WithMany().HasForeignKey(r => r.DestinationStationId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<FollowedJourney>(follow =>
        {
            follow.ToTable("followed_journeys", Schema);
            follow.HasKey(f => f.Id);
            follow.Property(f => f.Id).HasColumnName("id").ValueGeneratedNever();
            follow.Property(f => f.AccountId).HasColumnName("account_id");
            follow.Property(f => f.JourneyId).HasColumnName("journey_id");
            follow.Property(f => f.FollowedAt).HasColumnName("followed_at");
            // One follow per account and dated journey; following again is not a second row.
            follow.HasIndex(f => new { f.AccountId, f.JourneyId }).IsUnique();
            follow.HasOne<Account>().WithMany().HasForeignKey(f => f.AccountId).OnDelete(DeleteBehavior.Restrict);
            follow.HasOne<ScheduledJourney>().WithMany().HasForeignKey(f => f.JourneyId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
