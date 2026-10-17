namespace SmartRail.Api.Modules.Operations.Reports;

public static class PostgresTime
{
    // PostgreSQL keeps microseconds. A time rounded down to them reads back exactly as it was
    // written, so a response and every later read show the same instant.
    public static DateTimeOffset Round(DateTimeOffset time) =>
        new(time.UtcTicks - time.UtcTicks % 10, TimeSpan.Zero);
}
