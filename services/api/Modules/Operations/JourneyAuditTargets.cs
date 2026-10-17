using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Operations.Incidents;
using SmartRail.Api.Modules.Operations.Reports;

namespace SmartRail.Api.Modules.Operations;

// Everything the protected journey audit follows for one journey: its own entries, the entries of
// the reports sent on it and of the incidents those reports started. A report-backed action is
// audited against the report or incident itself, so the trail can follow one report to what came
// of it. An incident that stands alone belongs to no journey and has its own audit read.
public sealed class JourneyAuditTargets(ReportStore reports, IncidentStore incidents)
{
    public async Task<IReadOnlyList<AuditTargets>> ForJourneyAsync(Guid journeyId, CancellationToken cancellationToken) =>
    [
        new AuditTargets(StaffChangeEndpoints.JourneyTarget, [journeyId]),
        new AuditTargets(ReportSubmitter.ReportTarget, await reports.IdsForJourneyAsync(journeyId, cancellationToken)),
        new AuditTargets(IncidentOpener.IncidentTarget, await incidents.IdsForJourneyAsync(journeyId, cancellationToken)),
    ];
}
