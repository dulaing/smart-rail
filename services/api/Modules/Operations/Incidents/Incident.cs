namespace SmartRail.Api.Modules.Operations.Incidents;

// An incident has only two states. There is no way back from Resolved.
public static class IncidentStates
{
    public const string Open = "OPEN";
    public const string Resolved = "RESOLVED";

    public static readonly IReadOnlyList<string> All = [Open, Resolved];

    // The state named in a query string, in any letter case, or null when it names none.
    public static string? Parse(string? text) =>
        All.FirstOrDefault(state => string.Equals(state, text, StringComparison.OrdinalIgnoreCase));
}

// Control's own record that something needs attention. It can start from a driver's report or
// stand alone. Opening or resolving one never acknowledges a report or touches a published
// change: those are separate actions.
public sealed class Incident
{
    public Guid Id { get; init; }

    // Set when the incident started from a report; each report starts at most one incident.
    public Guid? SourceReportId { get; init; }

    public string Status { get; set; } = IncidentStates.Open;

    // For a report-backed incident, copied from the report.
    public required string ReasonCode { get; init; }

    public Guid OpenedById { get; init; }
    public DateTimeOffset OpenedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
