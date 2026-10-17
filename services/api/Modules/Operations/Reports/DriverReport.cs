using System.Diagnostics.CodeAnalysis;

namespace SmartRail.Api.Modules.Operations.Reports;

// The pilot's reason labels for a driver report. They are not a formal incident classification.
public static class ReasonCodes
{
    public const string TrainFault = "TRAIN_FAULT";
    public const string SignalDelay = "SIGNAL_DELAY";
    public const string Obstruction = "OBSTRUCTION";
    public const string CannotContinue = "CANNOT_CONTINUE";

    public static readonly IReadOnlyList<string> All = [TrainFault, SignalDelay, Obstruction, CannotContinue];

    public static bool IsValid([NotNullWhen(true)] string? code) => code is not null && All.Contains(code);
}

// What a stored report can show. "Not sent" never appears here: it exists only on a device.
public static class ReportStates
{
    public const string SentAwaitingControl = "SENT_AWAITING_CONTROL";
    public const string Acknowledged = "ACKNOWLEDGED";
}

// One field report a driver sent to Control. Receipt is the row existing; only a controller's
// explicit acknowledgement fills the two acknowledgement fields, always together.
public sealed class DriverReport
{
    public Guid Id { get; init; }
    public Guid JourneyId { get; init; }
    public Guid SubmittedById { get; init; }

    // The driver's own retry key, unique across all reports.
    public Guid ClientReportId { get; init; }

    public required string ReasonCode { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
    public Guid? AcknowledgedById { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }

    public string State => AcknowledgedAt is null ? ReportStates.SentAwaitingControl : ReportStates.Acknowledged;
}
