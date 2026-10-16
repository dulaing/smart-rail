using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Tests.Rail;

// What may be accepted as an observation, and which stored one counts as current.
// No database, host or sleeping: the clock is a fixed instant.
public class ObservationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Journey = Guid.NewGuid();
    private static readonly Guid From = Guid.NewGuid();
    private static readonly Guid To = Guid.NewGuid();

    private static ObservationRequest Valid(
        string? eventId = "evt-1", int? progress = 500, int? delay = 60, string? observedAt = "2026-10-14T15:29:30+05:30") =>
        new(eventId, Journey, From, To, progress, delay, observedAt);

    private static Observation Stored(
        string eventId, DateTimeOffset observedAt, DateTimeOffset? receivedAt = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            JourneyId = Journey,
            SourceEventId = eventId,
            FromStopId = From,
            ToStopId = To,
            ProgressPermille = 500,
            ReportedDelaySeconds = 60,
            ObservedAt = observedAt,
            ReceivedAt = receivedAt ?? observedAt.AddSeconds(1),
        };

    [Fact]
    public void A_complete_request_is_read_and_its_time_becomes_a_utc_instant()
    {
        var ok = ObservationRules.TryRead(Valid(), Now, out var input, out var error);

        Assert.True(ok, error);
        Assert.Equal("evt-1", input.SourceEventId);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 9, 59, 30, TimeSpan.Zero), input.ObservedAt);
        Assert.Equal(TimeSpan.Zero, input.ObservedAt.Offset);
    }

    [Fact]
    public void Observed_time_is_kept_to_the_microsecond_that_PostgreSQL_stores()
    {
        var request = Valid(observedAt: "2026-10-14T09:59:30.1234567Z");

        Assert.True(ObservationRules.TryRead(request, Now, out var input, out _));
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 9, 59, 30, TimeSpan.Zero).AddTicks(1234560), input.ObservedAt);
    }

    [Fact]
    public void Observed_time_may_be_now_but_not_after_it()
    {
        Assert.True(ObservationRules.TryRead(Valid(observedAt: "2026-10-14T10:00:00Z"), Now, out _, out _));
        Assert.False(ObservationRules.TryRead(Valid(observedAt: "2026-10-14T10:00:00.000001Z"), Now, out _, out var error));
        Assert.Contains("future", error);
    }

    [Theory]
    [InlineData("2026-10-14T09:00:00")]       // no offset
    [InlineData("2026-10-14 09:00:00Z")]      // not ISO 8601
    [InlineData("not a time")]
    [InlineData("2026-13-45T09:00:00Z")]
    [InlineData("")]
    [InlineData(null)]
    public void A_missing_or_malformed_timestamp_is_rejected(string? observedAt)
    {
        Assert.False(ObservationRules.TryRead(Valid(observedAt: observedAt), Now, out _, out var error));
        Assert.Contains("observedAt", error);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1001)]
    [InlineData(null)]
    public void Progress_must_be_present_and_within_0_to_1000(int? progress)
    {
        Assert.False(ObservationRules.TryRead(Valid(progress: progress), Now, out _, out var error));
        Assert.Contains("progressPermille", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public void Both_ends_of_the_progress_range_are_valid(int progress) =>
        Assert.True(ObservationRules.TryRead(Valid(progress: progress), Now, out _, out _));

    [Theory]
    [InlineData(-1)]
    [InlineData(null)]
    public void Delay_must_be_present_and_not_negative(int? delay)
    {
        Assert.False(ObservationRules.TryRead(Valid(delay: delay), Now, out _, out var error));
        Assert.Contains("reportedDelaySeconds", error);
    }

    [Fact]
    public void Zero_delay_is_valid()
    {
        Assert.True(ObservationRules.TryRead(Valid(delay: 0), Now, out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_event_id_is_required(string? eventId)
    {
        Assert.False(ObservationRules.TryRead(Valid(eventId: eventId), Now, out _, out var error));
        Assert.Contains("sourceEventId", error);
    }

    [Fact]
    public void An_event_id_longer_than_the_column_is_rejected()
    {
        var longest = new string('x', ObservationRules.MaxEventIdLength);

        Assert.True(ObservationRules.TryRead(Valid(eventId: longest), Now, out _, out _));
        Assert.False(ObservationRules.TryRead(Valid(eventId: longest + "x"), Now, out _, out _));
    }

    [Fact]
    public void Missing_ids_and_a_missing_body_are_rejected()
    {
        Assert.False(ObservationRules.TryRead(null, Now, out _, out _));
        Assert.False(ObservationRules.TryRead(Valid() with { JourneyId = null }, Now, out _, out _));
        Assert.False(ObservationRules.TryRead(Valid() with { JourneyId = Guid.Empty }, Now, out _, out _));
        Assert.False(ObservationRules.TryRead(Valid() with { FromStopId = null }, Now, out _, out _));
        Assert.False(ObservationRules.TryRead(Valid() with { ToStopId = null }, Now, out _, out _));
    }

    private static PatternStop Stop(int sequence) =>
        new() { Id = Guid.NewGuid(), StationId = Guid.NewGuid(), Sequence = sequence };

    [Fact]
    public void A_segment_must_join_neighbouring_stops_in_order_whatever_the_sequence_numbers()
    {
        var a = Stop(10);
        var b = Stop(20);
        var c = Stop(35);
        PatternStop[] stops = [c, a, b];

        bool Fits(PatternStop from, PatternStop to) =>
            ObservationRules.IsSegmentOf(new ObservationInput("e", Journey, from.Id, to.Id, 0, 0, Now), stops);

        Assert.True(Fits(a, b));
        Assert.True(Fits(b, c));
        Assert.False(Fits(a, c));   // skips a stop
        Assert.False(Fits(b, a));   // reversed
        Assert.False(Fits(a, a));
        Assert.False(ObservationRules.IsSegmentOf(
            new ObservationInput("e", Journey, a.Id, Guid.NewGuid(), 0, 0, Now), stops));
    }

    [Fact]
    public void The_same_event_ignores_receipt_time_but_not_any_reported_fact()
    {
        var input = new ObservationInput("evt-1", Journey, From, To, 500, 60, Now);
        var stored = Stored("evt-1", Now, receivedAt: Now.AddMinutes(5));

        Assert.True(ObservationRules.IsSameEvent(stored, input));
        Assert.False(ObservationRules.IsSameEvent(stored, input with { ProgressPermille = 501 }));
        Assert.False(ObservationRules.IsSameEvent(stored, input with { ReportedDelaySeconds = 61 }));
        Assert.False(ObservationRules.IsSameEvent(stored, input with { ObservedAt = Now.AddSeconds(-1) }));
        Assert.False(ObservationRules.IsSameEvent(stored, input with { FromStopId = Guid.NewGuid() }));
        Assert.False(ObservationRules.IsSameEvent(stored, input with { ToStopId = Guid.NewGuid() }));
        Assert.False(ObservationRules.IsSameEvent(stored, input with { JourneyId = Guid.NewGuid() }));
    }

    [Fact]
    public void Current_evidence_is_the_greatest_observed_time_not_the_latest_receipt()
    {
        var older = Stored("older", Now.AddSeconds(-60), receivedAt: Now.AddSeconds(30));
        var newer = Stored("newer", Now.AddSeconds(-10), receivedAt: Now.AddSeconds(-5));

        Assert.Same(newer, ObservationRules.Current([older, newer]));
        Assert.Same(newer, ObservationRules.Current([newer, older]));
    }

    [Fact]
    public void Equal_observed_times_are_separated_by_the_greater_event_id_compared_ordinally()
    {
        var upper = Stored("B", Now);
        var lower = Stored("a", Now);

        // Ordinally 'a' (97) is greater than 'B' (66); a culture-aware comparison says the opposite.
        Assert.Same(lower, ObservationRules.Current([upper, lower]));
        Assert.Same(lower, ObservationRules.Current([lower, upper]));
    }

    [Fact]
    public void No_observations_means_no_current_evidence()
    {
        Assert.Null(ObservationRules.Current([]));
    }

    [Theory]
    [InlineData("secret", "secret", true)]
    [InlineData("secret", "Secret", false)]
    [InlineData("secret", "secre", false)]
    [InlineData("secret", "", false)]
    [InlineData("secret", null, false)]
    [InlineData("", "", false)]
    [InlineData("", "anything", false)]
    [InlineData(null, "anything", false)]
    [InlineData(null, null, false)]
    public void A_service_key_matches_only_when_configured_and_equal(string? configured, string? presented, bool expected)
    {
        Assert.Equal(expected, ServiceKeys.Matches(configured, presented));
    }
}
