using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Modules.Rail;

// One stop occurrence with its schedule, estimates and platforms, as another module may show it.
public sealed record RunningDetailStop(
    Guid StopId,
    Guid StationId,
    string StationName,
    int Sequence,
    bool Served,
    DateTimeOffset? ScheduledArrival,
    DateTimeOffset? ScheduledDeparture,
    DateTimeOffset? EstimatedArrival,
    DateTimeOffset? EstimatedDeparture,
    string? PlannedPlatform,
    string? ConfirmedPlatform,
    DateTimeOffset? ConfirmedPlatformAt);

// What the public running read says about a journey, without the Rail types behind it. The
// freshness is as of ReadAt, the server clock; a client keeps ageing it from ObservedAt and the
// thresholds. A null ObservedAt means there is no observation to age.
public sealed record RunningDetail(
    Guid JourneyId,
    DateOnly ServiceDate,
    string TrainNumber,
    string TimetableKind,
    string TimetableLabel,
    DateTimeOffset? TimetableCheckedAt,
    string RunningLabel,
    int AgeingAfterSeconds,
    int UnknownAfterSeconds,
    string Freshness,
    DateTimeOffset? ObservedAt,
    Guid? LastKnownStationId,
    Guid? PositionFromStopId,
    Guid? PositionToStopId,
    int? ProgressPermille,
    int? DelaySeconds,
    string Availability,
    int? ConfirmedDelayMinutes,
    Guid? FinalStopId,
    IReadOnlyList<RunningDetailStop> Stops)
{
    // The server clock the detail was calculated at. Set by the reader.
    public DateTimeOffset ReadAt { get; init; }

    // The response header and text that carry the server clock on the public running read.
    public const string ServerTimeHeader = RunningEndpoints.ServerTimeHeader;

    public string ServerTime => RunningEndpoints.ServerTime(ReadAt);
}

// A journey on one service date in short: where it starts and ends and when.
public sealed record JourneySummary(
    Guid JourneyId,
    DateOnly ServiceDate,
    string TrainNumber,
    string TimetableLabel,
    string OriginName,
    DateTimeOffset? ScheduledDeparture,
    string DestinationName,
    DateTimeOffset? ScheduledArrival);

// The small door Operations uses for a protected briefing. It returns the same running answer as
// the public read, so the thresholds and the ageing rule stay in one place, and it never lets
// Operations read Rail's tables.
public interface IJourneyRunningReader
{
    // Null for an unknown journey.
    Task<RunningDetail?> ReadAsync(Guid journeyId, CancellationToken cancellationToken);

    // The listed journeys that run on the service date, in train-number order.
    Task<IReadOnlyList<JourneySummary>> ListAsync(
        IReadOnlyCollection<Guid> journeyIds, DateOnly serviceDate, CancellationToken cancellationToken);
}

public sealed class JourneyRunningReader(SmartRailDbContext db, RunningReader running, TimeProvider time) : IJourneyRunningReader
{
    public async Task<RunningDetail?> ReadAsync(Guid journeyId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var response = await running.ReadAsync(journeyId, now, cancellationToken);
        if (response is null)
        {
            return null;
        }

        var stationIds = response.Stops.Select(s => s.StationId).Distinct().ToList();
        var names = await db.Stations.AsNoTracking()
            .Where(s => stationIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.NameEn, cancellationToken);

        var position = response.CurrentPosition;
        return new RunningDetail(
            response.JourneyId,
            response.ServiceDate,
            response.TrainNumber,
            response.Timetable.Kind,
            response.Timetable.Label,
            response.Timetable.CheckedAt,
            response.RunningLabel,
            response.Thresholds.AgeingAfterSeconds,
            response.Thresholds.UnknownAfterSeconds,
            response.Freshness,
            response.LastObservation?.ObservedAt,
            response.LastObservation?.LastKnownStationId,
            position?.FromStopId,
            position?.ToStopId,
            position?.ProgressPermille,
            response.DelaySeconds,
            response.Availability,
            response.ConfirmedDelayMinutes,
            response.FinalStopId,
            response.Stops.Select(s => new RunningDetailStop(
                s.StopId, s.StationId, names.GetValueOrDefault(s.StationId, ""), s.Sequence, s.Served,
                s.ScheduledArrival, s.ScheduledDeparture, s.EstimatedArrival, s.EstimatedDeparture,
                s.PlannedPlatform, s.ConfirmedPlatform, s.ConfirmedPlatformAt)).ToList())
        {
            ReadAt = now,
        };
    }

    public async Task<IReadOnlyList<JourneySummary>> ListAsync(
        IReadOnlyCollection<Guid> journeyIds, DateOnly serviceDate, CancellationToken cancellationToken)
    {
        if (journeyIds.Count == 0)
        {
            return [];
        }

        var journeys = await db.Journeys.AsNoTracking()
            .Include(j => j.Pattern).ThenInclude(p => p.Stops)
            .Where(j => journeyIds.Contains(j.Id) && j.ServiceDate == serviceDate)
            .ToListAsync(cancellationToken);
        var names = await db.Stations.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.NameEn, cancellationToken);

        return journeys
            .Where(j => j.Pattern.RunsOn(j.ServiceDate) && j.Pattern.Stops.Count > 0)
            .OrderBy(j => j.Pattern.TrainNumber, StringComparer.Ordinal)
            .ThenBy(j => j.Id)
            .Select(j =>
            {
                var stops = j.Pattern.Stops.OrderBy(s => s.Sequence).ToList();
                var (_, departure) = ColomboTime.StopInstants(j.ServiceDate, stops[0]);
                var (arrival, _) = ColomboTime.StopInstants(j.ServiceDate, stops[^1]);
                return new JourneySummary(
                    j.Id,
                    j.ServiceDate,
                    j.Pattern.TrainNumber,
                    j.Pattern.SourceKind.Label(),
                    names.GetValueOrDefault(stops[0].StationId, ""),
                    departure,
                    names.GetValueOrDefault(stops[^1].StationId, ""),
                    arrival);
            })
            .ToList();
    }
}
