using SmartRail.Api.Modules.Operations.Changes;
using static SmartRail.Api.Tests.Operations.ChangeFixtures;

namespace SmartRail.Api.Tests.Operations;

// Which drafts may be previewed at all: each kind's own fields, nothing else, and only stop
// occurrences of this journey.
public class ChangeValidationTests
{
    private readonly Corridor corridor = new();
    private readonly Loop loop = new();

    private static void AssertInvalid(Outcome<ChangeDetails> result, string expectedWord)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(ProblemKind.Invalid, result.Problem!.Kind);
        Assert.Contains(expectedWord, result.Problem.Message);
    }

    [Fact]
    public void A_valid_draft_of_each_kind_is_accepted()
    {
        var delay = ChangeRules.ValidateDraft(Delay(25), corridor.Journey);
        var cancel = ChangeRules.ValidateDraft(Cancel(), corridor.Journey);
        var platform = ChangeRules.ValidateDraft(Platform(corridor.Peradeniya.Id, "2"), corridor.Journey);
        var earlyEnd = ChangeRules.ValidateDraft(EarlyEnd(corridor.Polgahawela.Id), corridor.Journey);

        Assert.Equal(ChangeDetails.Delay(25), delay.Value);
        Assert.Equal(ChangeDetails.Cancel(), cancel.Value);
        Assert.Equal(ChangeDetails.Platform(corridor.Peradeniya.Id, "2"), platform.Value);
        Assert.Equal(ChangeDetails.EarlyEnd(corridor.Polgahawela.Id), earlyEnd.Value);
    }

    [Fact]
    public void A_missing_body_or_unknown_kind_is_invalid()
    {
        AssertInvalid(ChangeRules.ValidateDraft(null, corridor.Journey), "body");
        AssertInvalid(ChangeRules.ValidateDraft(new ChangeDraft(null, null, null, null, null), corridor.Journey), "kind");
        AssertInvalid(ChangeRules.ValidateDraft(new ChangeDraft("REROUTE", null, null, null, null), corridor.Journey), "kind");
        AssertInvalid(ChangeRules.ValidateDraft(new ChangeDraft("delay", 5, null, null, null), corridor.Journey), "kind");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(600)]
    public void A_delay_of_zero_or_more_minutes_is_accepted(int minutes)
    {
        Assert.True(ChangeRules.ValidateDraft(Delay(minutes), corridor.Journey).Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public void A_delay_needs_a_non_negative_whole_number_of_minutes(int? minutes)
    {
        AssertInvalid(ChangeRules.ValidateDraft(Delay(minutes), corridor.Journey), "delayMinutes");
    }

    [Fact]
    public void A_platform_change_needs_a_nonblank_platform_and_a_target_stop()
    {
        var stop = corridor.Peradeniya.Id;

        AssertInvalid(ChangeRules.ValidateDraft(Platform(stop, null), corridor.Journey), "newPlatform");
        AssertInvalid(ChangeRules.ValidateDraft(Platform(stop, ""), corridor.Journey), "newPlatform");
        AssertInvalid(ChangeRules.ValidateDraft(Platform(stop, "   "), corridor.Journey), "newPlatform");
        AssertInvalid(ChangeRules.ValidateDraft(Platform(null, "2"), corridor.Journey), "targetStopId");
    }

    [Fact]
    public void A_platform_name_is_trimmed()
    {
        var result = ChangeRules.ValidateDraft(Platform(corridor.Peradeniya.Id, " 2A "), corridor.Journey);

        Assert.Equal("2A", result.Value!.NewPlatform);
    }

    [Fact]
    public void An_early_end_needs_a_final_stop()
    {
        AssertInvalid(ChangeRules.ValidateDraft(EarlyEnd(null), corridor.Journey), "finalStopId");
    }

    [Fact]
    public void A_cancellation_takes_no_type_specific_fields()
    {
        var stop = corridor.Veyangoda.Id;

        AssertInvalid(ChangeRules.ValidateDraft(new ChangeDraft("CANCEL", 5, null, null, null), corridor.Journey), "delayMinutes");
        AssertInvalid(ChangeRules.ValidateDraft(new ChangeDraft("CANCEL", null, "2", null, null), corridor.Journey), "newPlatform");
        AssertInvalid(ChangeRules.ValidateDraft(new ChangeDraft("CANCEL", null, null, stop, null), corridor.Journey), "targetStopId");
        AssertInvalid(ChangeRules.ValidateDraft(new ChangeDraft("CANCEL", null, null, null, stop), corridor.Journey), "finalStopId");
    }

    [Fact]
    public void Fields_of_another_kind_are_rejected_not_ignored()
    {
        var stop = corridor.Veyangoda.Id;

        AssertInvalid(ChangeRules.ValidateDraft(new ChangeDraft("DELAY", 5, "2", null, null), corridor.Journey), "newPlatform");
        AssertInvalid(ChangeRules.ValidateDraft(new ChangeDraft("DELAY", 5, null, stop, null), corridor.Journey), "targetStopId");
        AssertInvalid(ChangeRules.ValidateDraft(new ChangeDraft("PLATFORM", 5, "2", stop, null), corridor.Journey), "delayMinutes");
        AssertInvalid(ChangeRules.ValidateDraft(new ChangeDraft("PLATFORM", null, "2", stop, stop), corridor.Journey), "finalStopId");
        AssertInvalid(ChangeRules.ValidateDraft(new ChangeDraft("EARLY_END", null, null, stop, stop), corridor.Journey), "targetStopId");
        AssertInvalid(ChangeRules.ValidateDraft(new ChangeDraft("EARLY_END", null, "2", null, stop), corridor.Journey), "newPlatform");
    }

    [Fact]
    public void A_stop_of_another_journey_is_rejected()
    {
        var elsewhere = new Corridor().Peradeniya.Id;

        AssertInvalid(ChangeRules.ValidateDraft(Platform(elsewhere, "2"), corridor.Journey), "targetStopId");
        AssertInvalid(ChangeRules.ValidateDraft(EarlyEnd(elsewhere), corridor.Journey), "finalStopId");
        AssertInvalid(ChangeRules.ValidateDraft(Platform(Guid.Empty, "2"), corridor.Journey), "targetStopId");
    }

    [Fact]
    public void A_station_id_is_not_a_stop_id()
    {
        AssertInvalid(ChangeRules.ValidateDraft(Platform(corridor.Peradeniya.StationId, "2"), corridor.Journey), "targetStopId");
        AssertInvalid(ChangeRules.ValidateDraft(EarlyEnd(corridor.Polgahawela.StationId), corridor.Journey), "finalStopId");
        AssertInvalid(ChangeRules.ValidateDraft(EarlyEnd(loop.FirstB.StationId), loop.Journey), "finalStopId");
    }

    [Fact]
    public void An_early_end_must_be_an_intermediate_stop()
    {
        AssertInvalid(ChangeRules.ValidateDraft(EarlyEnd(corridor.Gampaha.Id), corridor.Journey), "intermediate");
        AssertInvalid(ChangeRules.ValidateDraft(EarlyEnd(corridor.Kandy.Id), corridor.Journey), "intermediate");
        Assert.True(ChangeRules.ValidateDraft(EarlyEnd(corridor.Veyangoda.Id), corridor.Journey).Succeeded);
        Assert.True(ChangeRules.ValidateDraft(EarlyEnd(corridor.Peradeniya.Id), corridor.Journey).Succeeded);
    }

    [Fact]
    public void An_early_end_must_be_at_a_stop_with_a_scheduled_arrival()
    {
        var noArrival = Stop("Odd", 2, arrival: false);
        var journey = new OperationsJourney(
            Guid.NewGuid(), [corridor.Gampaha, noArrival, corridor.Kandy]);

        AssertInvalid(ChangeRules.ValidateDraft(EarlyEnd(noArrival.Id), journey), "arrival");
    }

    [Fact]
    public void A_repeated_station_is_two_separate_targets()
    {
        var first = ChangeRules.ValidateDraft(Platform(loop.FirstB.Id, "5"), loop.Journey);
        var second = ChangeRules.ValidateDraft(Platform(loop.SecondB.Id, "6"), loop.Journey);
        var early = ChangeRules.ValidateDraft(EarlyEnd(loop.FirstB.Id), loop.Journey);

        Assert.Equal(loop.FirstB.Id, first.Value!.TargetStopId);
        Assert.Equal(loop.SecondB.Id, second.Value!.TargetStopId);
        Assert.Equal(loop.FirstB.Id, early.Value!.FinalStopId);
    }

    [Fact]
    public void A_withdrawal_must_name_a_known_active_publish()
    {
        var delay = Publish(1, ChangeDetails.Delay(10));
        var withdrawal = Withdraw(2, delay);
        var history = new[] { delay, withdrawal };
        var state = ConfirmedState.Fold(history);

        var missing = ChangeRules.ValidateWithdrawal(null, history, state);
        var unknown = ChangeRules.ValidateWithdrawal(Guid.NewGuid(), history, state);
        var ofAWithdrawal = ChangeRules.ValidateWithdrawal(withdrawal.Id, history, state);
        var alreadyWithdrawn = ChangeRules.ValidateWithdrawal(delay.Id, history, state);

        Assert.Equal(ProblemKind.Invalid, missing.Problem!.Kind);
        Assert.Equal(ProblemKind.NotFound, unknown.Problem!.Kind);
        Assert.Equal(ProblemKind.Invalid, ofAWithdrawal.Problem!.Kind);
        Assert.Equal(ProblemKind.Invalid, alreadyWithdrawn.Problem!.Kind);
    }

    [Fact]
    public void A_replaced_publish_cannot_be_withdrawn_but_its_replacement_can()
    {
        var tenMinutes = Publish(1, ChangeDetails.Delay(10));
        var twentyFive = Publish(2, ChangeDetails.Delay(25));
        var history = new[] { tenMinutes, twentyFive };
        var state = ConfirmedState.Fold(history);

        var replaced = ChangeRules.ValidateWithdrawal(tenMinutes.Id, history, state);
        var current = ChangeRules.ValidateWithdrawal(twentyFive.Id, history, state);

        Assert.Equal(ProblemKind.Invalid, replaced.Problem!.Kind);
        Assert.Equal(twentyFive, current.Value);
    }
}
