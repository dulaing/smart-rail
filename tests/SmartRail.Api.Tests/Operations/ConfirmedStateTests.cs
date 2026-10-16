using SmartRail.Api.Modules.Operations.Changes;
using static SmartRail.Api.Tests.Operations.ChangeFixtures;

namespace SmartRail.Api.Tests.Operations;

// Folding history in journey-version order, and what the folded decisions mean for each stop.
public class ConfirmedStateTests
{
    private readonly Corridor corridor = new();
    private readonly Loop loop = new();

    private JourneyOutcome Outcome(params ServiceChange[] history) =>
        JourneyOutcome.From(corridor.Journey, ConfirmedState.Fold(history));

    [Fact]
    public void No_history_leaves_the_schedule_unchanged()
    {
        var outcome = Outcome();

        Assert.Equal(ServiceAvailability.AsScheduled, outcome.Availability);
        Assert.Null(outcome.ConfirmedDelayMinutes);
        Assert.Null(outcome.FinalStopId);
        Assert.False(outcome.OrderClosed);
        Assert.Empty(outcome.BlockedPickupStopIds);
        Assert.All(outcome.Stops, s =>
        {
            Assert.True(s.Served);
            Assert.Null(s.ConfirmedPlatform);
            Assert.Null(s.ConfirmedDelayMinutes);
        });
        Assert.Equal("3", Of(outcome, corridor.Polgahawela).Platform);
    }

    [Fact]
    public void History_is_folded_by_journey_version_not_list_order()
    {
        var ten = Publish(1, ChangeDetails.Delay(10));
        var twentyFive = Publish(2, ChangeDetails.Delay(25));

        Assert.Equal(25, Outcome(twentyFive, ten).ConfirmedDelayMinutes);
        Assert.Equal(25, Outcome(ten, twentyFive).ConfirmedDelayMinutes);
    }

    [Fact]
    public void A_newer_publish_replaces_the_same_kind()
    {
        var outcome = Outcome(
            Publish(1, ChangeDetails.Delay(10)),
            Publish(2, ChangeDetails.EarlyEnd(corridor.Peradeniya.Id)),
            Publish(3, ChangeDetails.Delay(25)),
            Publish(4, ChangeDetails.EarlyEnd(corridor.Veyangoda.Id)));

        Assert.Equal(25, outcome.ConfirmedDelayMinutes);
        Assert.Equal(corridor.Veyangoda.Id, outcome.FinalStopId);
    }

    [Fact]
    public void Platform_changes_replace_per_stop_and_leave_other_stops_alone()
    {
        var outcome = Outcome(
            Publish(1, ChangeDetails.Platform(corridor.Veyangoda.Id, "5")),
            Publish(2, ChangeDetails.Platform(corridor.Peradeniya.Id, "6")),
            Publish(3, ChangeDetails.Platform(corridor.Veyangoda.Id, "7")));

        Assert.Equal("7", Of(outcome, corridor.Veyangoda).Platform);
        Assert.Equal("6", Of(outcome, corridor.Peradeniya).Platform);
        Assert.Equal("3", Of(outcome, corridor.Polgahawela).Platform);
    }

    [Fact]
    public void Withdrawing_a_replacement_does_not_revive_the_older_change()
    {
        var ten = Publish(1, ChangeDetails.Delay(10));
        var twentyFive = Publish(2, ChangeDetails.Delay(25));

        var outcome = Outcome(ten, twentyFive, Withdraw(3, twentyFive));

        Assert.Null(outcome.ConfirmedDelayMinutes);
        Assert.All(outcome.Stops, s => Assert.Null(s.ConfirmedDelayMinutes));
    }

    [Fact]
    public void Withdrawing_a_platform_change_restores_the_scheduled_platform_only_there()
    {
        var veyangoda = Publish(1, ChangeDetails.Platform(corridor.Veyangoda.Id, "5"));
        var peradeniya = Publish(2, ChangeDetails.Platform(corridor.Peradeniya.Id, "6"));

        var outcome = Outcome(veyangoda, peradeniya, Withdraw(3, veyangoda));

        Assert.Equal("1", Of(outcome, corridor.Veyangoda).Platform);
        Assert.Null(Of(outcome, corridor.Veyangoda).ConfirmedPlatform);
        Assert.Equal("6", Of(outcome, corridor.Peradeniya).Platform);
    }

    [Fact]
    public void A_withdrawal_preserves_other_kinds()
    {
        var delay = Publish(1, ChangeDetails.Delay(15));
        var platform = Publish(2, ChangeDetails.Platform(corridor.Peradeniya.Id, "6"));
        var earlyEnd = Publish(3, ChangeDetails.EarlyEnd(corridor.Polgahawela.Id));

        var outcome = Outcome(delay, platform, earlyEnd, Withdraw(4, earlyEnd));

        Assert.Equal(ServiceAvailability.AsScheduled, outcome.Availability);
        Assert.Equal(15, outcome.ConfirmedDelayMinutes);
        Assert.Equal("6", Of(outcome, corridor.Peradeniya).Platform);
        Assert.All(outcome.Stops, s => Assert.True(s.Served));
    }

    [Fact]
    public void A_withdrawal_of_a_replaced_or_unknown_change_is_ignored_when_folding()
    {
        var ten = Publish(1, ChangeDetails.Delay(10));
        var twentyFive = Publish(2, ChangeDetails.Delay(25));
        var stray = new ServiceChange(Guid.NewGuid(), 4, ChangeAction.Withdraw, new ChangeDetails(ChangeKind.Delay), Guid.NewGuid());

        var outcome = Outcome(ten, twentyFive, Withdraw(3, ten), stray);

        Assert.Equal(25, outcome.ConfirmedDelayMinutes);
    }

    [Fact]
    public void An_active_list_names_only_the_changes_in_force()
    {
        var ten = Publish(1, ChangeDetails.Delay(10));
        var twentyFive = Publish(2, ChangeDetails.Delay(25));
        var cancel = Publish(3, ChangeDetails.Cancel());
        var state = ConfirmedState.Fold([ten, twentyFive, cancel, Withdraw(4, cancel)]);

        Assert.Equal([twentyFive], state.Active);
        Assert.False(state.IsActive(ten));
        Assert.True(state.IsActive(twentyFive));
        Assert.False(state.IsActive(cancel));
    }

    [Fact]
    public void An_early_end_keeps_the_final_stop_and_earlier_stops_and_drops_later_ones()
    {
        var outcome = Outcome(Publish(1, ChangeDetails.EarlyEnd(corridor.Polgahawela.Id)));

        Assert.Equal(ServiceAvailability.EndsEarly, outcome.Availability);
        Assert.Equal(corridor.Polgahawela.Id, outcome.FinalStopId);
        Assert.True(Of(outcome, corridor.Gampaha).Served);
        Assert.True(Of(outcome, corridor.Veyangoda).Served);
        Assert.True(Of(outcome, corridor.Polgahawela).Served);
        Assert.False(Of(outcome, corridor.Peradeniya).Served);
        Assert.False(Of(outcome, corridor.Kandy).Served);
        Assert.Equal([corridor.Peradeniya.Id, corridor.Kandy.Id], outcome.BlockedPickupStopIds);
        Assert.False(outcome.OrderClosed);
    }

    [Fact]
    public void An_unserved_stop_carries_no_delay_but_a_served_one_does()
    {
        var outcome = Outcome(
            Publish(1, ChangeDetails.Delay(20)),
            Publish(2, ChangeDetails.EarlyEnd(corridor.Polgahawela.Id)));

        Assert.Equal(20, Of(outcome, corridor.Polgahawela).ConfirmedDelayMinutes);
        Assert.Null(Of(outcome, corridor.Peradeniya).ConfirmedDelayMinutes);
        Assert.Equal(20, outcome.ConfirmedDelayMinutes);
    }

    [Fact]
    public void Cancellation_overrides_availability_but_keeps_the_early_end_underneath()
    {
        var earlyEnd = Publish(1, ChangeDetails.EarlyEnd(corridor.Polgahawela.Id));
        var cancel = Publish(2, ChangeDetails.Cancel());

        var cancelled = Outcome(earlyEnd, cancel);
        var restored = Outcome(earlyEnd, cancel, Withdraw(3, cancel));

        Assert.Equal(ServiceAvailability.Cancelled, cancelled.Availability);
        Assert.True(cancelled.OrderClosed);
        Assert.All(cancelled.Stops, s => Assert.False(s.Served));
        Assert.Equal([corridor.Peradeniya.Id, corridor.Kandy.Id], cancelled.BlockedPickupStopIds);

        Assert.Equal(ServiceAvailability.EndsEarly, restored.Availability);
        Assert.False(restored.OrderClosed);
        Assert.True(Of(restored, corridor.Polgahawela).Served);
        Assert.False(Of(restored, corridor.Peradeniya).Served);
    }

    [Fact]
    public void Withdrawing_a_cancellation_alone_returns_to_the_schedule()
    {
        var cancel = Publish(1, ChangeDetails.Cancel());

        var outcome = Outcome(cancel, Withdraw(2, cancel));

        Assert.Equal(ServiceAvailability.AsScheduled, outcome.Availability);
        Assert.All(outcome.Stops, s => Assert.True(s.Served));
    }

    [Fact]
    public void A_confirmed_platform_survives_a_cancellation_and_an_early_end()
    {
        var outcome = Outcome(
            Publish(1, ChangeDetails.Platform(corridor.Peradeniya.Id, "6")),
            Publish(2, ChangeDetails.EarlyEnd(corridor.Veyangoda.Id)),
            Publish(3, ChangeDetails.Cancel()));

        Assert.Equal("6", Of(outcome, corridor.Peradeniya).Platform);
    }

    [Fact]
    public void A_repeated_station_is_ended_at_one_occurrence_only()
    {
        var outcome = JourneyOutcome.From(
            loop.Journey,
            ConfirmedState.Fold([Publish(1, ChangeDetails.EarlyEnd(loop.FirstB.Id))]));

        Assert.True(Of(outcome, loop.FirstB).Served);
        Assert.False(Of(outcome, loop.SecondB).Served);
        Assert.False(Of(outcome, loop.Middle).Served);
        Assert.Equal([loop.Middle.Id, loop.SecondB.Id, loop.End.Id], outcome.BlockedPickupStopIds);
    }

    [Fact]
    public void A_platform_change_at_one_occurrence_leaves_the_other_visit_alone()
    {
        var outcome = JourneyOutcome.From(
            loop.Journey,
            ConfirmedState.Fold([Publish(1, ChangeDetails.Platform(loop.SecondB.Id, "9"))]));

        Assert.Equal("1", Of(outcome, loop.FirstB).Platform);
        Assert.Equal("9", Of(outcome, loop.SecondB).Platform);
    }
}
