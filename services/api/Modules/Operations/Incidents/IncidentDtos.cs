using SmartRail.Api.Modules.Access;

namespace SmartRail.Api.Modules.Operations.Incidents;

// The body of a new incident: a report to start from, or a reason code for one that stands
// alone. Exactly one of the two.
public sealed record IncidentRequest(Guid? SourceReportId, string? ReasonCode);

public sealed record IncidentDto(
    Guid IncidentId,
    Guid? SourceReportId,
    string ReasonCode,
    string State,
    Guid OpenedById,
    string? OpenedByName,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ResolvedAt,
    IReadOnlyList<Guid> ChangeIds)
{
    public static IncidentDto From(
        Incident incident, IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> changeIds) =>
        new(
            incident.Id,
            incident.SourceReportId,
            incident.ReasonCode,
            incident.Status,
            incident.OpenedById,
            names.GetValueOrDefault(incident.OpenedById),
            incident.OpenedAt,
            incident.ResolvedAt,
            changeIds.GetValueOrDefault(incident.Id) ?? []);
}

public sealed record IncidentPageDto(IReadOnlyList<IncidentDto> Items, string? NextCursor);

// One oldest-first page of an incident's audit trail. nextCursor continues to the next page and
// is null at the end of the history; complete says whether this page reached that end.
public sealed record IncidentAuditDto(
    Guid IncidentId, IReadOnlyList<AuditRecord> Entries, string? NextCursor, bool Complete);
