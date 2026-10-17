using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Operations.Reports;

namespace SmartRail.Api.Modules.Operations;

// Everything the protected journey audit follows for one journey: its own entries and the
// entries of the reports sent on it. A report-backed action is audited against the report, so
// the trail can follow one report to what came of it.
public sealed class JourneyAuditTargets(ReportStore reports)
{
    public async Task<IReadOnlyList<AuditTargets>> ForJourneyAsync(Guid journeyId, CancellationToken cancellationToken) =>
    [
        new AuditTargets(StaffChangeEndpoints.JourneyTarget, [journeyId]),
        new AuditTargets(ReportSubmitter.ReportTarget, await reports.IdsForJourneyAsync(journeyId, cancellationToken)),
    ];
}
