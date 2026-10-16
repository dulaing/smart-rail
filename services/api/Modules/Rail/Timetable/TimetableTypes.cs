namespace SmartRail.Api.Modules.Rail.Timetable;

// Bit mask stored in service_patterns.operating_days_mask.
[Flags]
public enum OperatingDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    Every = 127,
}

// Where a timetable came from. Stored as the codes PUBLISHED and FICTIONAL_DEMO.
public enum SourceKind
{
    Published,
    FictionalDemo,
}

public static class SourceKinds
{
    public const string PublishedCode = "PUBLISHED";
    public const string FictionalDemoCode = "FICTIONAL_DEMO";

    public static string ToCode(this SourceKind kind) =>
        kind == SourceKind.Published ? PublishedCode : FictionalDemoCode;

    public static SourceKind FromCode(string code) => code switch
    {
        PublishedCode => SourceKind.Published,
        FictionalDemoCode => SourceKind.FictionalDemo,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown source kind."),
    };

    // The label a passenger sees. A timetable is not a live train feed.
    public static string Label(this SourceKind kind) =>
        kind == SourceKind.Published ? "Published timetable" : "SmartRail simulation";
}

public sealed class Station
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameEn { get; init; }
    public required string NameSi { get; init; }
    public required string NameTa { get; init; }
}

// One stop of a pattern. Times are local Colombo wall-clock times; DayOffset
// moves a stop after midnight (1 = the day after the service date). The first stop has no
// arrival and the last stop has no departure.
public sealed class PatternStop
{
    public Guid Id { get; init; }
    public Guid PatternId { get; init; }
    public Guid StationId { get; init; }
    public int Sequence { get; init; }
    public TimeOnly? ArrivalLocal { get; init; }
    public TimeOnly? DepartureLocal { get; init; }
    public int DayOffset { get; init; }
    public string? PlannedPlatform { get; init; }
}

public sealed class ServicePattern
{
    public Guid Id { get; init; }
    public required string TrainNumber { get; init; }
    public SourceKind SourceKind { get; init; }
    public string? SourceUrl { get; init; }
    public DateTimeOffset? SourceCheckedAt { get; init; }
    public OperatingDays OperatingDays { get; init; } = OperatingDays.Every;
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public List<PatternStop> Stops { get; init; } = [];

    // Whether the pattern is allowed to run on a service date.
    public bool RunsOn(DateOnly serviceDate)
    {
        if (serviceDate < EffectiveFrom || (EffectiveTo is { } to && serviceDate > to))
        {
            return false;
        }

        var day = (OperatingDays)(1 << (((int)serviceDate.DayOfWeek + 6) % 7));
        return OperatingDays.HasFlag(day);
    }
}

// A pattern run on one Colombo service date.
public sealed class ScheduledJourney
{
    public Guid Id { get; init; }
    public Guid PatternId { get; init; }
    public DateOnly ServiceDate { get; init; }
    public string? DemoSeedKey { get; init; }
    public required ServicePattern Pattern { get; init; }
}

// Turns a service date plus local time and day offset into an instant.
public static class ColomboTime
{
    // Sri Lanka has no daylight saving time, so a fixed UTC+05:30 offset is exact for the pilot.
    public static readonly TimeSpan Offset = TimeSpan.FromMinutes(330);

    public static DateTimeOffset ToInstant(DateOnly serviceDate, TimeOnly local, int dayOffset) =>
        new(serviceDate.AddDays(dayOffset).ToDateTime(local), Offset);

    public static DateOnly ServiceDateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(Offset).DateTime);

    // Scheduled arrival and departure of one stop occurrence. One day offset serves both
    // times, so an arrival "after" the departure (23:58 in, 00:03 out) happened the day before.
    public static (DateTimeOffset? Arrival, DateTimeOffset? Departure) StopInstants(
        DateOnly serviceDate, PatternStop stop)
    {
        DateTimeOffset? arrival = stop.ArrivalLocal is { } a ? ToInstant(serviceDate, a, stop.DayOffset) : null;
        DateTimeOffset? departure = stop.DepartureLocal is { } d ? ToInstant(serviceDate, d, stop.DayOffset) : null;

        if (arrival > departure)
        {
            arrival = arrival.Value.AddDays(-1);
        }

        return (arrival, departure);
    }
}
