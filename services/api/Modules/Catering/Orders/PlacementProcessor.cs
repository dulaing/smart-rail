using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Catering.Callbacks;
using SmartRail.Api.Modules.Catering.Provider;

namespace SmartRail.Api.Modules.Catering.Orders;

public sealed class PlacementOptions
{
    public const string Section = "Catering:Placement";

    // How long after the last attempt a lookup that finds nothing is trusted. A request that
    // timed out may still be in flight at the provider; after this long it has finished one way
    // or the other.
    public int SettleSeconds { get; set; } = 30;

    public int MaxBackoffSeconds { get; set; } = 60;
    public int PollSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 20;
    public bool WorkerEnabled { get; set; } = true;
}

// What was asked for, kept on the link while the outcome is unknown.
public sealed record StoredRequest(string CollectionPointId, Guid JourneyId, Guid PickupStopId, IReadOnlyList<RequestedItem> Items);

// Tries to place one pending order with the provider, or to find out what happened to it. The
// request that created the link calls it once; the retry loop calls it for every link that is due.
// A timeout is never read as a refusal: the link stays pending, with the same link ID on every try,
// until the provider answers or a safe reconciliation says nothing was ever placed.
public sealed class PlacementProcessor(
    SmartRailDbContext db,
    IRailBitesClient provider,
    PickupAssessor assessor,
    OrderLinkWrites writes,
    TimeProvider time,
    IOptions<PlacementOptions> options,
    ILogger<PlacementProcessor> logger)
{
    private const int ContractFaultBackoffSeconds = 300;

    public async Task<OrderLink> AttemptAsync(Guid linkId, CancellationToken cancellationToken)
    {
        var link = await db.OrderLinks.AsNoTracking().SingleAsync(l => l.Id == linkId, cancellationToken);
        if (link.PlacementState != PlacementStates.Pending)
        {
            return link;
        }

        var now = time.GetUtcNow();
        if (link.RequestPayload is null)
        {
            // Reopened by a callback for an order we had called refused. There is nothing left to
            // place; only the provider's order (with its code and items) is fetched.
            await FetchAsync(link, now, cancellationToken);
            return await db.OrderLinks.AsNoTracking().SingleAsync(l => l.Id == linkId, cancellationToken);
        }

        var assessment = await assessor.AssessAsync(link.JourneyId, link.PickupStopId, cancellationToken);

        if (assessment.Problem is not null)
        {
            // No longer eligible for a new order, so do not place one. Only find out whether the
            // earlier attempt got through; if it did, the train-change event will correct it.
            await ReconcileAsync(link, assessment.Problem, now, cancellationToken);
        }
        else
        {
            await PlaceAsync(link, assessment, now, cancellationToken);
        }

        return await db.OrderLinks.AsNoTracking().SingleAsync(l => l.Id == linkId, cancellationToken);
    }

    private async Task PlaceAsync(OrderLink link, PickupAssessment assessment, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<StoredRequest>(link.RequestPayload!, ProviderJson.Options)!;
        await writes.MarkAttemptAsync(link.Id, now, cancellationToken);

        var body = new PlaceOrderBody(request.CollectionPointId, assessment.Journey!, assessment.Pickup!, request.Items);
        using var activity = CateringMetrics.Source.StartActivity("catering.placement.attempt");
        activity?.SetTag("catering.attempt", link.AttemptCount + 1);
        var response = await provider.PlaceAsync(link.Id, body, cancellationToken);
        activity?.SetTag("catering.result", response.Outcome.ToString());
        CateringMetrics.PlacementAttempts.Add(1, new KeyValuePair<string, object?>("result", response.Outcome switch
        {
            ProviderOutcome.Success => "confirmed",
            ProviderOutcome.Refused when response.HttpStatus is 422 or 404 => "refused",
            _ => "retry",
        }));

        switch (response.Outcome)
        {
            case ProviderOutcome.Success:
                await writes.ApplyProviderOrderAsync(link.Id, response.Value!, now, cancellationToken);
                break;

            case ProviderOutcome.Refused when response.HttpStatus is 422 or 404:
                // The provider looked at this order and said no, with a reason that stays on the link.
                await writes.RejectAsync(link.Id, response.Error!.Code, now, cancellationToken);
                break;

            case ProviderOutcome.Refused:
                // 400 or 409: SmartRail and RailBites disagree about the contract or the state of
                // the journey. Keep the order, say why, and look again later.
                logger.LogError("RailBites refused placement for link {LinkId} with {Code}.", link.Id, response.Error!.Code);
                var fault = $"{response.Error.Code}: {response.Error.Message}";
                await writes.DeferAsync(link.Id, now.AddSeconds(ContractFaultBackoffSeconds), fault[..Math.Min(fault.Length, 200)], now, cancellationToken);
                break;

            default:
                await writes.DeferAsync(link.Id, now.AddSeconds(BackoffSeconds(link.AttemptCount + 1)), response.Error?.Code, now, cancellationToken);
                break;
        }
    }

    private async Task FetchAsync(OrderLink link, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var found = await provider.LookupAsync(link.Id, cancellationToken);
        if (found.Outcome == ProviderOutcome.Success)
        {
            await writes.ApplyProviderOrderAsync(link.Id, found.Value!, now, cancellationToken);
        }
        else
        {
            await writes.DeferAsync(link.Id, now.AddSeconds(BackoffSeconds(link.AttemptCount + 1)), found.Error?.Code, now, cancellationToken);
        }
    }

    private async Task ReconcileAsync(OrderLink link, string problem, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var settle = TimeSpan.FromSeconds(options.Value.SettleSeconds);
        var found = await provider.LookupAsync(link.Id, cancellationToken);

        switch (found.Outcome)
        {
            case ProviderOutcome.Success:
                await writes.ApplyProviderOrderAsync(link.Id, found.Value!, now, cancellationToken);
                break;

            case ProviderOutcome.NotFound when link.LastAttemptAt is null || now - link.LastAttemptAt >= settle:
                // Nothing was ever sent, or the last attempt is old enough that it cannot still
                // be arriving. Only now is it safe to tell the passenger it was not placed.
                await writes.RejectAsync(link.Id, problem, now, cancellationToken);
                break;

            case ProviderOutcome.NotFound:
                await writes.DeferAsync(link.Id, link.LastAttemptAt!.Value + settle, "RECONCILING", now, cancellationToken);
                break;

            default:
                await writes.DeferAsync(link.Id, now.AddSeconds(BackoffSeconds(link.AttemptCount + 1)), found.Error?.Code, now, cancellationToken);
                break;
        }
    }

    // Increasing delays, capped: 2, 4, 8 ... seconds up to the configured maximum.
    private int BackoffSeconds(int attempt) =>
        (int)Math.Min(options.Value.MaxBackoffSeconds, Math.Pow(2, Math.Min(attempt, 16)));

    // Every pending link that is due, oldest first. Returns how many were tried.
    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await db.OrderLinks.AsNoTracking()
            .Where(l => l.PlacementState == PlacementStates.Pending && l.NextAttemptAt <= now)
            .OrderBy(l => l.NextAttemptAt)
            .Select(l => l.Id)
            .Take(options.Value.BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var id in due)
        {
            await AttemptAsync(id, cancellationToken);
        }

        return due.Count;
    }
}
