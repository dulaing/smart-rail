using SmartRail.Api.Modules.Operations.Changes;

namespace SmartRail.Api.Tests.Operations;

// Small journeys for the change rules: no database, login or clock.
internal static class ChangeFixtures
{
    // Gampaha -> Veyangoda -> Polgahawela -> Peradeniya -> Kandy. The first stop has no
    // arrival, as in the timetable; the others do.
    public sealed class Corridor
    {
        public Corridor()
        {
            Gampaha = Stop("Gampaha", 1, arrival: false, platform: "2");
            Veyangoda = Stop("Veyangoda", 2, platform: "1");
            Polgahawela = Stop("Polgahawela", 3, platform: "3");
            Peradeniya = Stop("Peradeniya", 4, platform: "1");
            Kandy = Stop("Kandy", 5, platform: "4");
            Journey = new OperationsJourney(Guid.NewGuid(), [Kandy, Gampaha, Peradeniya, Veyangoda, Polgahawela]);
        }

        public JourneyStop Gampaha { get; }
        public JourneyStop Veyangoda { get; }
        public JourneyStop Polgahawela { get; }
        public JourneyStop Peradeniya { get; }
        public JourneyStop Kandy { get; }
        public OperationsJourney Journey { get; }
    }

    // A -> B -> C -> B -> D. Station B is two stop occurrences with two IDs.
    public sealed class Loop
    {
        public Loop()
        {
            var b = Guid.NewGuid();
            Start = Stop("Alpha", 1, arrival: false, platform: "1");
            FirstB = Stop("Beta", 2, platform: "1", stationId: b);
            Middle = Stop("Gamma", 3, platform: "1");
            SecondB = Stop("Beta", 4, platform: "2", stationId: b);
            End = Stop("Delta", 5, platform: "1");
            Journey = new OperationsJourney(Guid.NewGuid(), [Start, FirstB, Middle, SecondB, End]);
        }

        public JourneyStop Start { get; }
        public JourneyStop FirstB { get; }
        public JourneyStop Middle { get; }
        public JourneyStop SecondB { get; }
        public JourneyStop End { get; }
        public OperationsJourney Journey { get; }
    }

    public static JourneyStop Stop(
        string name, int sequence, bool arrival = true, string? platform = null, Guid? stationId = null) =>
        new(Guid.NewGuid(), stationId ?? Guid.NewGuid(), name, sequence, arrival, platform);

    public static ServiceChange Publish(int version, ChangeDetails details) =>
        new(Guid.NewGuid(), version, ChangeAction.Publish, details);

    public static ServiceChange Withdraw(int version, ServiceChange target) =>
        new(Guid.NewGuid(), version, ChangeAction.Withdraw, new ChangeDetails(target.Details.Kind), target.Id);

    public static ChangeDraft Delay(int? minutes) => new("DELAY", minutes, null, null, null);

    public static ChangeDraft Cancel() => new("CANCEL", null, null, null, null);

    public static ChangeDraft Platform(Guid? stopId, string? platform) => new("PLATFORM", null, platform, stopId, null);

    public static ChangeDraft EarlyEnd(Guid? stopId) => new("EARLY_END", null, null, null, stopId);

    public static StopOutcome Of(JourneyOutcome outcome, JourneyStop stop) =>
        outcome.Stops.Single(s => s.StopId == stop.Id);
}
