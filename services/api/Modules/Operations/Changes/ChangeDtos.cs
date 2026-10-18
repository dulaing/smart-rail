using SmartRail.Api.Modules.Access;

namespace SmartRail.Api.Modules.Operations.Changes;

// The body of the preview and confirm routes: either a draft to publish or the ID of a
// published change to withdraw, never both. Confirm also carries the version the preview showed.
// A draft may name the incident that explains it; a withdrawal never does, because it keeps the
// incident of the change it withdraws.
public sealed record ChangeRequest(ChangeDraft? Draft, Guid? WithdrawChangeId, int? PreviewedVersion, Guid? IncidentId = null);

// The answer to a confirmed change: its ID, the journey's new version and the state now in force,
// and the incident it carries, if any. This is a staff response; public reads never show it.
public sealed record ChangeConfirmedDto(Guid ChangeId, int JourneyVersion, ConfirmedStateDto State, Guid? IncidentId = null);

// One oldest-first page of a journey's audit trail. nextCursor continues to the next page and is
// null at the end of the history; complete says whether this page reached that end.
public sealed record JourneyAuditDto(
    Guid JourneyId, IReadOnlyList<AuditRecord> Entries, string? NextCursor, bool Complete);

public sealed record ChangeDetailsDto(
    string Kind, int? DelayMinutes, string? NewPlatform, Guid? TargetStopId, Guid? FinalStopId)
{
    public static ChangeDetailsDto From(ChangeDetails d) =>
        new(d.Kind.ToCode(), d.DelayMinutes, d.NewPlatform, d.TargetStopId, d.FinalStopId);
}

public sealed record JourneyOutcomeDto(
    string Availability, int? ConfirmedDelayMinutes, Guid? FinalStopId, bool OrderClosed,
    IReadOnlyList<Guid> BlockedPickupStopIds, IReadOnlyList<StopOutcome> Stops)
{
    public static JourneyOutcomeDto From(JourneyOutcome o) =>
        new(o.Availability.ToCode(), o.ConfirmedDelayMinutes, o.FinalStopId, o.OrderClosed, o.BlockedPickupStopIds, o.Stops);
}

// What a controller reviews before confirming, as the API returns it.
public sealed record ChangePreviewDto(
    string Action,
    ChangeDetailsDto Change,
    Guid? WithdrawsChangeId,
    int PreviewedVersion,
    JourneyOutcomeDto Before,
    JourneyOutcomeDto After,
    IReadOnlyList<StopOutcome> AffectedStops,
    IReadOnlyList<ScreenImpact> Screens,
    FoodImpact Food,
    EarlyEndImpact? EarlyEnd,
    Guid? IncidentId = null)
{
    public static ChangePreviewDto From(ChangePreview p, Guid? incidentId = null) =>
        new(
            p.Action.ToCode(),
            ChangeDetailsDto.From(p.Change),
            p.WithdrawsChangeId,
            p.PreviewedVersion,
            JourneyOutcomeDto.From(p.Before),
            JourneyOutcomeDto.From(p.After),
            p.AffectedStops,
            p.Screens,
            p.Food,
            p.EarlyEnd,
            incidentId);
}
