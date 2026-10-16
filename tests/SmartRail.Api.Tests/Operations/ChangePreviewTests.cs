using SmartRail.Api.Modules.Operations.Changes;
using static SmartRail.Api.Tests.Operations.ChangeFixtures;

namespace SmartRail.Api.Tests.Operations;

// What a controller sees before confirming: before and after, who and what is affected.
public class ChangePreviewTests
{
    private readonly Corridor corridor = new();
    private readonly Loop loop = new();

    private ChangePreview Publish(ChangeDraft draft, int version = 0, ServiceChange[]? history = null, int? orders = null)
    {
        var result = ChangePreviewer.PreviewPublish(corridor.Journey, version, history ?? [], draft, orders);
        Assert.True(result.Succeeded, result.Problem?.Message);
        return result.Value!;
    }

    private ChangePreview Withdrawal(ServiceChange target, ServiceChange[] history, int version, int? orders = null)
    {
        var result = ChangePreviewer.PreviewWithdrawal(corridor.Journey, version, history, target.Id, orders);
        Assert.True(result.Succeeded, result.Problem?.Message);
        return result.Value!;
    }

    private IEnumerable<Guid> AffectedIds(ChangePreview preview) => preview.AffectedStops.Select(s => s.StopId);

    [Fact]
    public void An_invalid_draft_is_not_previewed()
    {
        var result = ChangePreviewer.PreviewPublish(corridor.Journey, 0, [], Delay(-5));

        Assert.False(result.Succeeded);
        Assert.Equal(ProblemKind.Invalid, result.Problem!.Kind);
    }

    [Fact]
    public void The_previewed_version_is_the_version_given_and_the_history_is_untouched()
    {
        var delay = ChangeFixtures.Publish(7, ChangeDetails.Delay(10));
        ServiceChange[] history = [delay];

        var preview = Publish(Delay(25), version: 7, history);

        Assert.Equal(7, preview.PreviewedVersion);
        Assert.Equal([delay], history);
        Assert.Equal(ChangeAction.Publish, preview.Action);
        Assert.Equal(ChangeDetails.Delay(25), preview.Change);
        Assert.Null(preview.WithdrawsChangeId);
    }

    [Fact]
    public void A_delay_shows_before_and_after_for_every_served_stop()
    {
        var preview = Publish(Delay(25));

        Assert.Null(preview.Before.ConfirmedDelayMinutes);
        Assert.Equal(25, preview.After.ConfirmedDelayMinutes);
        Assert.Equal(corridor.Journey.Stops.Select(s => s.Id), AffectedIds(preview));
        Assert.All(preview.AffectedStops, s => Assert.Equal(25, s.ConfirmedDelayMinutes));
        Assert.Null(preview.EarlyEnd);
    }

    [Fact]
    public void A_delay_that_replaces_another_shows_the_difference()
    {
        var ten = ChangeFixtures.Publish(1, ChangeDetails.Delay(10));

        var preview = Publish(Delay(25), version: 1, [ten]);

        Assert.Equal(10, preview.Before.ConfirmedDelayMinutes);
        Assert.Equal(25, preview.After.ConfirmedDelayMinutes);
    }

    [Fact]
    public void Repeating_what_is_already_in_force_affects_nothing()
    {
        var ten = ChangeFixtures.Publish(1, ChangeDetails.Delay(10));

        var preview = Publish(Delay(10), version: 1, [ten]);

        Assert.Empty(preview.AffectedStops);
        Assert.Empty(preview.Screens);
        Assert.False(preview.Food.EtaChanges);
        Assert.Equal(0, preview.Food.AffectedOrderCount);
    }

    [Fact]
    public void A_platform_change_affects_one_stop_and_one_board()
    {
        var preview = Publish(Platform(corridor.Peradeniya.Id, "6"));

        var stop = Assert.Single(preview.AffectedStops);
        Assert.Equal(corridor.Peradeniya.Id, stop.StopId);
        Assert.Equal("1", Of(preview.Before, corridor.Peradeniya).Platform);
        Assert.Equal("6", Of(preview.After, corridor.Peradeniya).Platform);

        var board = Assert.Single(preview.Screens, s => s.Surface == Surfaces.StationBoard);
        Assert.Equal([corridor.Peradeniya.StationId], board.StationIds);
    }

    [Fact]
    public void Every_surface_is_listed_when_the_journey_changes()
    {
        var preview = Publish(Cancel());

        Assert.Equal(
            [Surfaces.PassengerApp, Surfaces.StationBoard, Surfaces.DriverBriefing],
            preview.Screens.Select(s => s.Surface));
        var board = preview.Screens.Single(s => s.Surface == Surfaces.StationBoard);
        Assert.Equal(corridor.Journey.Stops.Select(s => s.StationId), board.StationIds);
    }

    [Fact]
    public void A_cancellation_closes_ordering_and_makes_every_stop_unserved()
    {
        var preview = Publish(Cancel());

        Assert.Equal(ServiceAvailability.Cancelled, preview.After.Availability);
        Assert.All(preview.AffectedStops, s => Assert.False(s.Served));
        Assert.False(preview.Food.OrderClosedBefore);
        Assert.True(preview.Food.OrderClosedAfter);
    }

    [Fact]
    public void An_early_end_names_the_new_final_station_and_lists_the_unserved_later_stops()
    {
        var preview = Publish(EarlyEnd(corridor.Polgahawela.Id));

        var impact = preview.EarlyEnd!;
        Assert.Equal(corridor.Polgahawela.Id, impact.FinalStopId);
        Assert.Equal(corridor.Polgahawela.StationId, impact.FinalStationId);
        Assert.Equal("Polgahawela", impact.FinalStationName);
        Assert.Equal([corridor.Peradeniya.Id, corridor.Kandy.Id], impact.UnservedStops.Select(s => s.StopId));
        Assert.All(impact.UnservedStops, s => Assert.False(s.Served));

        // Only the later stops change; the final stop and earlier ones stay valid.
        Assert.Equal([corridor.Peradeniya.Id, corridor.Kandy.Id], AffectedIds(preview));
    }

    [Fact]
    public void An_early_end_also_updates_the_board_at_its_new_final_station()
    {
        var preview = Publish(EarlyEnd(corridor.Polgahawela.Id));

        var board = preview.Screens.Single(s => s.Surface == Surfaces.StationBoard);
        Assert.Contains(corridor.Polgahawela.StationId, board.StationIds);
        Assert.Contains(corridor.Peradeniya.StationId, board.StationIds);
        Assert.Contains(corridor.Kandy.StationId, board.StationIds);
        Assert.DoesNotContain(corridor.Gampaha.StationId, board.StationIds);
    }

    [Fact]
    public void An_early_end_blocks_pickup_at_later_stops_and_reports_the_order_count_given()
    {
        var preview = Publish(EarlyEnd(corridor.Polgahawela.Id), orders: 3);

        Assert.Equal([corridor.Peradeniya.Id, corridor.Kandy.Id], preview.Food.NewlyBlockedStopIds);
        Assert.Empty(preview.Food.ReopenedStopIds);
        Assert.False(preview.Food.OrderClosedAfter);
        Assert.Equal(3, preview.Food.AffectedOrderCount);
    }

    [Fact]
    public void An_unavailable_order_count_is_unknown_not_zero()
    {
        var preview = Publish(EarlyEnd(corridor.Polgahawela.Id), orders: null);

        Assert.Null(preview.Food.AffectedOrderCount);
    }

    [Fact]
    public void A_change_with_no_food_effect_has_a_known_zero_count()
    {
        var preview = Publish(Platform(corridor.Peradeniya.Id, "6"), orders: null);

        Assert.Empty(preview.Food.NewlyBlockedStopIds);
        Assert.False(preview.Food.EtaChanges);
        Assert.Equal(0, preview.Food.AffectedOrderCount);
    }

    [Fact]
    public void A_delay_changes_the_eta_without_changing_eligibility()
    {
        var preview = Publish(Delay(25), orders: null);

        Assert.True(preview.Food.EtaChanges);
        Assert.Empty(preview.Food.NewlyBlockedStopIds);
        Assert.False(preview.Food.OrderClosedAfter);
        Assert.Null(preview.Food.AffectedOrderCount);
    }

    [Fact]
    public void Cancelling_over_an_early_end_blocks_nothing_new_but_closes_ordering()
    {
        var earlyEnd = ChangeFixtures.Publish(1, ChangeDetails.EarlyEnd(corridor.Polgahawela.Id));

        var preview = Publish(Cancel(), version: 1, [earlyEnd], orders: 2);

        Assert.Empty(preview.Food.NewlyBlockedStopIds);
        Assert.True(preview.Food.OrderClosedAfter);
        Assert.Equal(2, preview.Food.AffectedOrderCount);
        Assert.Equal([corridor.Gampaha.Id, corridor.Veyangoda.Id, corridor.Polgahawela.Id], AffectedIds(preview));
    }

    [Fact]
    public void Withdrawing_an_early_end_restores_only_the_stops_it_removed()
    {
        var delay = ChangeFixtures.Publish(1, ChangeDetails.Delay(15));
        var platform = ChangeFixtures.Publish(2, ChangeDetails.Platform(corridor.Peradeniya.Id, "6"));
        var earlyEnd = ChangeFixtures.Publish(3, ChangeDetails.EarlyEnd(corridor.Polgahawela.Id));
        ServiceChange[] history = [delay, platform, earlyEnd];

        var preview = Withdrawal(earlyEnd, history, version: 3);

        Assert.Equal(ChangeAction.Withdraw, preview.Action);
        Assert.Equal(earlyEnd.Id, preview.WithdrawsChangeId);
        Assert.Equal(earlyEnd.Details, preview.Change);
        Assert.Equal(3, preview.PreviewedVersion);
        Assert.Equal([corridor.Peradeniya.Id, corridor.Kandy.Id], AffectedIds(preview));
        Assert.All(preview.AffectedStops, s => Assert.True(s.Served));

        // The delay and the independent platform change are still in force afterwards.
        Assert.Equal(15, preview.After.ConfirmedDelayMinutes);
        Assert.Equal("6", Of(preview.After, corridor.Peradeniya).Platform);
        Assert.Equal([corridor.Peradeniya.Id, corridor.Kandy.Id], preview.Food.ReopenedStopIds);
        Assert.Empty(preview.Food.NewlyBlockedStopIds);
        Assert.Null(preview.EarlyEnd);
    }

    [Fact]
    public void Withdrawing_a_delay_that_replaced_another_removes_the_delay_entirely()
    {
        var ten = ChangeFixtures.Publish(1, ChangeDetails.Delay(10));
        var twentyFive = ChangeFixtures.Publish(2, ChangeDetails.Delay(25));

        var preview = Withdrawal(twentyFive, [ten, twentyFive], version: 2);

        Assert.Equal(25, preview.Before.ConfirmedDelayMinutes);
        Assert.Null(preview.After.ConfirmedDelayMinutes);
        Assert.True(preview.Food.EtaChanges);
    }

    [Fact]
    public void Withdrawing_a_cancellation_over_an_early_end_reopens_ordering_but_keeps_the_blocked_stops()
    {
        var earlyEnd = ChangeFixtures.Publish(1, ChangeDetails.EarlyEnd(corridor.Polgahawela.Id));
        var cancel = ChangeFixtures.Publish(2, ChangeDetails.Cancel());

        var preview = Withdrawal(cancel, [earlyEnd, cancel], version: 2);

        Assert.True(preview.Food.OrderClosedBefore);
        Assert.False(preview.Food.OrderClosedAfter);
        Assert.Equal(corridor.Journey.Stops.Take(3).Select(s => s.Id), AffectedIds(preview));
        Assert.Equal([corridor.Peradeniya.Id, corridor.Kandy.Id], preview.After.BlockedPickupStopIds);
    }

    [Fact]
    public void A_withdrawal_that_cannot_be_applied_is_refused()
    {
        var ten = ChangeFixtures.Publish(1, ChangeDetails.Delay(10));
        var twentyFive = ChangeFixtures.Publish(2, ChangeDetails.Delay(25));
        ServiceChange[] history = [ten, twentyFive, Withdraw(3, twentyFive)];

        var unknown = ChangePreviewer.PreviewWithdrawal(corridor.Journey, 3, history, Guid.NewGuid());
        var replaced = ChangePreviewer.PreviewWithdrawal(corridor.Journey, 3, history, ten.Id);
        var duplicate = ChangePreviewer.PreviewWithdrawal(corridor.Journey, 3, history, twentyFive.Id);
        var missing = ChangePreviewer.PreviewWithdrawal(corridor.Journey, 3, history, null);

        Assert.Equal(ProblemKind.NotFound, unknown.Problem!.Kind);
        Assert.Equal(ProblemKind.Invalid, replaced.Problem!.Kind);
        Assert.Equal(ProblemKind.Invalid, duplicate.Problem!.Kind);
        Assert.Equal(ProblemKind.Invalid, missing.Problem!.Kind);
    }

    [Fact]
    public void A_repeated_station_early_end_names_the_right_occurrence()
    {
        var result = ChangePreviewer.PreviewPublish(loop.Journey, 0, [], EarlyEnd(loop.FirstB.Id));

        var preview = result.Value!;
        Assert.Equal(loop.FirstB.Id, preview.EarlyEnd!.FinalStopId);
        Assert.Equal([loop.Middle.Id, loop.SecondB.Id, loop.End.Id], preview.EarlyEnd.UnservedStops.Select(s => s.StopId));
        Assert.DoesNotContain(loop.FirstB.Id, preview.AffectedStops.Select(s => s.StopId));
        Assert.Equal(preview.EarlyEnd.FinalStationId, loop.SecondB.StationId);
    }
}
