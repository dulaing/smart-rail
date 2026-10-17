using SmartRail.Api.Modules.Access;

namespace SmartRail.Api.Modules.Operations.Reports;

// The body of a new report. Everything else, including who sent it and when, is the server's.
public sealed record ReportRequest(Guid? ClientReportId, string? ReasonCode);

// A report as its driver sees it. The state shows server receipt on its own; the
// acknowledgement fields are empty until a controller explicitly acknowledges it.
public sealed record DriverReportDto(
    Guid ReportId,
    Guid ClientReportId,
    Guid JourneyId,
    string ReasonCode,
    DateTimeOffset ReceivedAt,
    string State,
    Guid? AcknowledgedById,
    string? AcknowledgedByName,
    DateTimeOffset? AcknowledgedAt)
{
    public static DriverReportDto From(DriverReport report, IReadOnlyDictionary<Guid, string> names) =>
        new(
            report.Id,
            report.ClientReportId,
            report.JourneyId,
            report.ReasonCode,
            report.ReceivedAt,
            report.State,
            report.AcknowledgedById,
            report.AcknowledgedById is { } id && names.TryGetValue(id, out var name) ? name : null,
            report.AcknowledgedAt);
}

public sealed record DriverReportPageDto(IReadOnlyList<DriverReportDto> Items, string? NextCursor);

// A report as Control sees it: who sent it and who, if anyone, acknowledged it. The driver's retry
// key stays with the driver.
public sealed record ControlReportDto(
    Guid ReportId,
    Guid JourneyId,
    string ReasonCode,
    Guid SubmittedById,
    string? SubmittedByName,
    DateTimeOffset ReceivedAt,
    string State,
    Guid? AcknowledgedById,
    string? AcknowledgedByName,
    DateTimeOffset? AcknowledgedAt)
{
    public static ControlReportDto From(DriverReport report, IReadOnlyDictionary<Guid, string> names) =>
        new(
            report.Id,
            report.JourneyId,
            report.ReasonCode,
            report.SubmittedById,
            names.GetValueOrDefault(report.SubmittedById),
            report.ReceivedAt,
            report.State,
            report.AcknowledgedById,
            report.AcknowledgedById is { } id ? names.GetValueOrDefault(id) : null,
            report.AcknowledgedAt);
}

public sealed record ControlReportPageDto(IReadOnlyList<ControlReportDto> Items, string? NextCursor);
