using SmartRail.Api.Modules.Rail;
using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Modules.Rail.Timetable;

namespace SmartRail.Api.Tests.Rail;

// The live next-train rule with fixed instants: no database, clock or HTTP.
public class LiveNextTrainsTests
{
    private static readonly Guid Gampaha = Guid.NewGuid();
    private static readonly Guid Ragama = Guid.NewGuid();
    private static readonly Guid Fort = Guid.NewGuid();

    // 2026-10-14 is a Wednesday.
    private static readonly DateOnly Wednesday = new(2026, 10, 14);
    private static readonly DateOnly Thursday = new(2026, 10, 15);

    private static DateTimeOffset At(int hour, int minute, int second = 0, DateOnly? date = null) =>
        new((date ?? Wednesday).ToDateTime(new TimeOnly(hour, minute, second)), ColomboTime.Offset);

    private static PatternStop Stop(Guid station, int sequence, string? arrival, string? departure, int dayOffset = 0, string? platform = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            StationId = station,
            Sequence = sequence,
            ArrivalLocal = arrival is null ? null : TimeOnly.Parse(arrival),
            DepartureLocal = departure is null ? null : TimeOnly.Parse(departure),
            DayOffset = dayOffset,
            PlannedPlatform = platform,
        };

    private static ScheduledJourney Journey(string trainNumber, DateOnly date, params PatternStop[] stops)
    {
        var pattern = new ServicePattern
        {
            Id = Guid.NewGuid(),
            TrainNumber = trainNumber,
            SourceKind = SourceKind.FictionalDemo,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Stops = [.. stops],
        };
        return new ScheduledJourney { Id = Guid.NewGuid(), PatternId = pattern.Id, ServiceDate = date, Pattern = pattern };
    }

    // Gampaha 08:00 -> Ragama 08:10/08:11 -> Fort 08:40, shifted by the given minutes.
    private static ScheduledJourney Standard(string trainNumber = "T1", int shiftMinutes = 0, DateOnly? date = null)
    {
        string Time(string t) => TimeOnly.Parse(t).AddMinutes(shiftMinutes).ToString("HH:mm");
        return Journey(trainNumber, date ?? Wednesday,
            Stop(Gampaha, 1, null, Time("08:00"), platform: "2"),
            Stop(Ragama, 2, Time("08:10"), Time("08:11"), platform: "1"),
            Stop(Fort, 3, Time("08:40"), null, platform: "4"));
    }

    private static PatternStop StopAt(ScheduledJourney journey, int index) =>
        journey.Pattern.Stops.OrderBy(s => s.Sequence).ElementAt(index);

    // Between two neighbouring stops, observed at the given instant.
    private static RunningObservation Seen(ScheduledJourney journey, int fromIndex, int progress, DateTimeOffset observedAt, int delaySeconds = 0) =>
        new(StopAt(journey, fromIndex).Id, StopAt(journey, fromIndex + 1).Id, progress, delaySeconds, observedAt);

    private static LiveJourney Live(ScheduledJourney journey, RunningObservation? observation = null, ConfirmedOverrides? overrides = null) =>
        new(journey, observation, overrides ?? ConfirmedOverrides.None);

    private static ConfirmedOverrides Confirmed(
        int? delay = null, bool cancelled = false, Guid? finalStop = null, DateTimeOffset? at = null, int version = 1,
        params (Guid Stop, string Platform, DateTimeOffset At)[] platforms) =>
        new(delay, cancelled, finalStop,
            platforms.ToDictionary(p => p.Stop, p => new ConfirmedPlatform(p.Platform, p.At)),
            delay is null ? null : at, cancelled ? at : null, finalStop is null ? null : at, version);

    [Fact]
    public void A_delayed_train_stays_after_its_scheduled_departure_and_the_scheduled_read_has_dropped_it()
    {
        // Ragama departure is 08:11. At 08:14 it is overdue, but the train is on its way there, 10 minutes late.
        var journey = Standard();
        var now = At(8, 14);
        var seen = Seen(journey, 0, 800, At(8, 13, 40), delaySeconds: 600);

        var scheduled = NextTrainFinder.Find([journey], Ragama, Fort, now);
        var live = LiveNextTrains.Find([Live(journey, seen)], Ragama, Fort, now);

        Assert.Empty(scheduled);
        var train = Assert.Single(live.Trains);
        Assert.Equal(At(8, 11), train.OriginScheduledDeparture);
        Assert.Equal(At(8, 21), train.OriginEstimatedDeparture);
        Assert.Equal(At(8, 50), train.DestinationEstimatedArrival);
        Assert.Equal("CURRENT", train.Freshness);
        Assert.False(train.Overdue);
    }

    [Fact]
    public void Observed_progress_past_the_origin_removes_the_train_even_when_the_observation_is_old()
    {
        var journey = Standard();
        var left = Seen(journey, 1, 100, At(8, 12));

        var soon = LiveNextTrains.Find([Live(journey, left)], Ragama, Fort, At(8, 12, 30));
        var muchLater = LiveNextTrains.Find([Live(journey, left)], Ragama, Fort, At(8, 30));

        Assert.Empty(soon.Trains);
        Assert.Empty(muchLater.Trains);
    }

    [Fact]
    public void A_train_standing_at_the_origin_has_not_left_it_yet()
    {
        var journey = Standard();
        var standing = Seen(journey, 0, 1000, At(8, 10, 30));

        var result = LiveNextTrains.Find([Live(journey, standing)], Ragama, Fort, At(8, 10, 40));

        Assert.Single(result.Trains);
    }

    [Fact]
    public void Evidence_that_does_not_sit_on_the_journey_proves_nothing()
    {
        var journey = Standard();
        var nonsense = new RunningObservation(StopAt(journey, 2).Id, StopAt(journey, 0).Id, 500, 0, At(8, 12));

        var result = LiveNextTrains.Find([Live(journey, nonsense)], Ragama, Fort, At(8, 12, 30));

        Assert.True(Assert.Single(result.Trains).Overdue);
    }

    [Fact]
    public void An_overdue_train_without_evidence_is_kept_but_never_on_time_and_dropped_after_its_arrival()
    {
        var journey = Standard();

        var overdue = LiveNextTrains.Find([Live(journey)], Ragama, Fort, At(8, 20));
        var train = Assert.Single(overdue.Trains);

        Assert.True(train.Overdue);
        Assert.Equal("UNKNOWN", train.Freshness);
        Assert.Null(train.OriginEstimatedDeparture);
        Assert.Null(train.DestinationEstimatedArrival);
        Assert.False(train.EarliestArrival);
        Assert.Equal(At(8, 11), train.OriginScheduledDeparture);

        Assert.Single(LiveNextTrains.Find([Live(journey)], Ragama, Fort, At(8, 40)).Trains);
        Assert.Empty(LiveNextTrains.Find([Live(journey)], Ragama, Fort, At(8, 40, 1)).Trains);
    }

    [Fact]
    public void A_confirmed_delay_extends_how_long_an_overdue_train_is_kept_but_adds_no_estimate()
    {
        var journey = Standard();
        var delayed = Confirmed(delay: 30, at: At(8, 5));

        var result = LiveNextTrains.Find([Live(journey, overrides: delayed)], Ragama, Fort, At(8, 50));

        var train = Assert.Single(result.Trains);
        Assert.True(train.Overdue);
        Assert.Null(train.OriginEstimatedDeparture);
        Assert.Null(train.DestinationEstimatedArrival);
        Assert.Equal(30, train.ConfirmedDelayMinutes);
        Assert.Equal(At(8, 5), train.ConfirmedDelayAt);
    }

    [Fact]
    public void An_upcoming_train_with_no_evidence_shows_its_schedule_and_is_not_overdue()
    {
        var journey = Standard();

        var result = LiveNextTrains.Find([Live(journey)], Gampaha, Fort, At(7, 30));

        var train = Assert.Single(result.Trains);
        Assert.False(train.Overdue);
        Assert.Equal("UNKNOWN", train.Freshness);
        Assert.Null(train.LastObservedAt);
        Assert.Null(train.OriginEstimatedDeparture);
        Assert.Equal(At(8, 0), train.OriginScheduledDeparture);
        Assert.Equal(At(8, 40), train.DestinationScheduledArrival);
        Assert.Equal("2", train.PlannedPlatform);
    }

    [Fact]
    public void Stale_evidence_removes_the_estimate_and_says_unknown_rather_than_on_time()
    {
        var journey = Standard();
        var seen = Seen(journey, 0, 500, At(8, 5));

        var ageing = LiveNextTrains.Find([Live(journey, seen)], Ragama, Fort, At(8, 5).AddSeconds(200));
        var unknown = LiveNextTrains.Find([Live(journey, seen)], Ragama, Fort, At(8, 5).AddSeconds(240));

        var ageingTrain = Assert.Single(ageing.Trains);
        Assert.Equal("AGEING", ageingTrain.Freshness);
        Assert.NotNull(ageingTrain.DestinationEstimatedArrival);

        var unknownTrain = Assert.Single(unknown.Trains);
        Assert.Equal("UNKNOWN", unknownTrain.Freshness);
        Assert.Null(unknownTrain.OriginEstimatedDeparture);
        Assert.Null(unknownTrain.DestinationEstimatedArrival);
        Assert.Equal(At(8, 5), unknownTrain.LastObservedAt);
    }

    [Fact]
    public void A_confirmed_platform_stays_on_the_train_with_its_time_when_the_evidence_is_unknown()
    {
        var journey = Standard();
        var platform = Confirmed(at: At(7, 55), platforms: [(StopAt(journey, 0).Id, "5", At(7, 55))]);

        var result = LiveNextTrains.Find([Live(journey, overrides: platform)], Gampaha, Fort, At(7, 58));

        var train = Assert.Single(result.Trains);
        Assert.Equal("2", train.PlannedPlatform);
        Assert.Equal("5", train.ConfirmedPlatform);
        Assert.Equal(At(7, 55), train.ConfirmedPlatformAt);
        Assert.Equal("UNKNOWN", train.Freshness);
    }

    [Fact]
    public void A_cancelled_train_is_never_a_choice_but_keeps_a_notice()
    {
        var cancelled = Standard("CANCELLED-1");
        var running = Standard("RUNNING-2", shiftMinutes: 20);
        var stamp = At(7, 40);

        var result = LiveNextTrains.Find(
            [Live(cancelled, overrides: Confirmed(cancelled: true, at: stamp, version: 3)), Live(running)],
            Gampaha, Fort, At(7, 45));

        Assert.Equal("RUNNING-2", Assert.Single(result.Trains).TrainNumber);
        var notice = Assert.Single(result.Notices);
        Assert.Equal("CANCELLED", notice.Kind);
        Assert.Equal(cancelled.Id, notice.JourneyId);
        Assert.Equal(At(8, 0), notice.OriginScheduledDeparture);
        Assert.Equal(stamp, notice.ConfirmedAt);
        Assert.Equal(3, notice.ConfirmedVersion);
        Assert.Null(notice.FinalStopId);
    }

    [Fact]
    public void A_cancelled_train_that_has_already_left_on_paper_gives_no_notice()
    {
        var cancelled = Standard();

        var result = LiveNextTrains.Find(
            [Live(cancelled, overrides: Confirmed(cancelled: true, at: At(7, 40)))], Gampaha, Fort, At(8, 30));

        Assert.Empty(result.Trains);
        Assert.Empty(result.Notices);
    }

    [Fact]
    public void An_early_end_before_the_destination_is_a_notice_and_after_it_stays_a_choice()
    {
        var journey = Standard();
        var endsAtRagama = Confirmed(finalStop: StopAt(journey, 1).Id, at: At(7, 50), version: 2);
        var now = At(7, 55);

        var toFort = LiveNextTrains.Find([Live(journey, overrides: endsAtRagama)], Gampaha, Fort, now);
        var toRagama = LiveNextTrains.Find([Live(journey, overrides: endsAtRagama)], Gampaha, Ragama, now);

        Assert.Empty(toFort.Trains);
        var notice = Assert.Single(toFort.Notices);
        Assert.Equal("ENDS_EARLY", notice.Kind);
        Assert.Equal(StopAt(journey, 1).Id, notice.FinalStopId);
        Assert.Equal(At(7, 50), notice.ConfirmedAt);

        var served = Assert.Single(toRagama.Trains);
        Assert.Empty(toRagama.Notices);
        Assert.Equal("ENDS_EARLY", served.Availability);
        Assert.Equal(StopAt(journey, 1).Id, served.FinalStopId);
    }

    [Fact]
    public void The_earliest_arrival_is_marked_only_when_every_choice_has_a_current_estimate()
    {
        var early = Standard("A");
        var late = Standard("B", shiftMinutes: 10);
        var now = At(7, 59);
        // Both trains are seen between Gampaha and Ragama; B reports a 20 minute delay.
        var seenEarly = Seen(early, 0, 100, At(7, 58, 40));
        var seenLate = Seen(late, 0, 100, At(7, 58, 40), delaySeconds: 1200);

        var compared = LiveNextTrains.Find([Live(early, seenEarly), Live(late, seenLate)], Ragama, Fort, now);

        Assert.Equal(["A", "B"], compared.Trains.Select(t => t.TrainNumber));
        Assert.Equal([true, false], compared.Trains.Select(t => t.EarliestArrival));

        var oneUnknown = LiveNextTrains.Find([Live(early, seenEarly), Live(late)], Ragama, Fort, now);
        Assert.All(oneUnknown.Trains, t => Assert.False(t.EarliestArrival));

        var single = LiveNextTrains.Find([Live(early, seenEarly)], Ragama, Fort, now);
        Assert.False(Assert.Single(single.Trains).EarliestArrival);

        var twin = Standard("C");
        var tie = LiveNextTrains.Find(
            [Live(early, seenEarly), Live(twin, Seen(twin, 0, 100, At(7, 58, 40)))], Ragama, Fort, now);
        Assert.All(tie.Trains, t => Assert.False(t.EarliestArrival));
    }

    [Fact]
    public void Ordering_uses_the_estimate_while_it_exists_and_the_schedule_otherwise()
    {
        var delayed = Standard("DELAYED");
        var next = Standard("NEXT", shiftMinutes: 9);
        var now = At(8, 0);
        // DELAYED is due at Ragama 08:11 but is 40 minutes late, so it leaves about 08:51, after NEXT at 08:20.
        var seen = Seen(delayed, 0, 500, At(7, 59, 30), delaySeconds: 2400);

        var result = LiveNextTrains.Find([Live(delayed, seen), Live(next)], Ragama, Fort, now);

        Assert.Equal(["NEXT", "DELAYED"], result.Trains.Select(t => t.TrainNumber));
    }

    [Fact]
    public void The_whole_candidate_set_is_inspected_before_taking_three()
    {
        var late = Standard("LATE", shiftMinutes: -10);
        var others = new[] { "B", "C", "D" }.Select((n, i) => Standard(n, shiftMinutes: 5 * (i + 1))).ToArray();
        var now = At(7, 55);
        // LATE is scheduled first at Ragama (08:01) but is an hour late, so it must not take a place from the others.
        var seen = Seen(late, 0, 500, At(7, 54, 50), delaySeconds: 3600);

        var result = LiveNextTrains.Find(
            [Live(late, seen), .. others.Select(j => Live(j))], Ragama, Fort, now);

        Assert.Equal(["B", "C", "D"], result.Trains.Select(t => t.TrainNumber));
    }

    [Fact]
    public void Only_three_notices_are_kept_in_departure_order()
    {
        var cancelled = Enumerable.Range(0, 5)
            .Select(i => Standard($"X{i}", shiftMinutes: i * 5)).ToArray();

        var result = LiveNextTrains.Find(
            cancelled.Select(j => Live(j, overrides: Confirmed(cancelled: true, at: At(7, 0)))),
            Gampaha, Fort, At(7, 30));

        Assert.Empty(result.Trains);
        Assert.Equal(["X0", "X1", "X2"], result.Notices.Select(n => n.TrainNumber));
    }

    [Fact]
    public void A_repeated_station_offers_the_second_occurrence_once_the_first_is_proven_left()
    {
        // Gampaha 08:00, Fort 08:40/08:45, Gampaha 09:30/09:35, Ragama 09:45.
        var loop = Journey("loop", Wednesday,
            Stop(Gampaha, 1, null, "08:00"),
            Stop(Fort, 2, "08:40", "08:45"),
            Stop(Gampaha, 3, "09:30", "09:35"),
            Stop(Ragama, 4, "09:45", null));
        var leftFirstGampaha = Seen(loop, 0, 300, At(8, 10));

        var before = LiveNextTrains.Find([Live(loop)], Gampaha, Ragama, At(7, 0));
        var after = LiveNextTrains.Find([Live(loop, leftFirstGampaha)], Gampaha, Ragama, At(8, 12));

        Assert.Equal(StopAt(loop, 0).Id, Assert.Single(before.Trains).OriginStopId);
        var second = Assert.Single(after.Trains);
        Assert.Equal(StopAt(loop, 2).Id, second.OriginStopId);
        Assert.Equal(StopAt(loop, 3).Id, second.DestinationStopId);
        Assert.Equal(At(9, 35), second.OriginScheduledDeparture);
    }

    [Fact]
    public void An_overnight_train_that_has_not_left_yet_stays_from_the_previous_service_date_until_proven_gone()
    {
        // Wednesday's service leaves Fort 23:50 and reaches Gampaha 00:40 on Thursday.
        var night = Journey("night", Wednesday,
            Stop(Fort, 1, null, "23:50"),
            Stop(Gampaha, 2, "00:40", null, dayOffset: 1));
        var tonight = Journey("night", Thursday,
            Stop(Fort, 1, null, "23:50"),
            Stop(Gampaha, 2, "00:40", null, dayOffset: 1));
        var now = At(0, 10, date: Thursday);

        var waiting = LiveNextTrains.Find([Live(tonight), Live(night)], Fort, Gampaha, now);

        Assert.Equal([Wednesday, Thursday], waiting.Trains.Select(t => t.ServiceDate));
        Assert.True(waiting.Trains[0].Overdue);
        Assert.False(waiting.Trains[1].Overdue);

        var gone = Seen(night, 0, 300, At(0, 5, date: Thursday));
        var left = LiveNextTrains.Find([Live(tonight), Live(night, gone)], Fort, Gampaha, now);
        Assert.Equal([Thursday], left.Trains.Select(t => t.ServiceDate));
    }

    [Fact]
    public void A_journey_that_does_not_run_that_day_or_visit_the_stations_gives_nothing()
    {
        var weekdays = Standard();
        var pattern = weekdays.Pattern;
        var closed = new ScheduledJourney
        {
            Id = Guid.NewGuid(), PatternId = pattern.Id, ServiceDate = new DateOnly(2025, 12, 31), Pattern = pattern,
        };

        Assert.Empty(LiveNextTrains.Find([Live(closed)], Gampaha, Fort, At(7, 0)).Trains);
        Assert.Empty(LiveNextTrains.Find([Live(weekdays)], Gampaha, Guid.NewGuid(), At(7, 0)).Trains);
        Assert.Empty(LiveNextTrains.Find([], Gampaha, Fort, At(7, 0)).Trains);
    }
}
