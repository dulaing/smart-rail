using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Briefings;
using SmartRail.Api.Modules.Operations.Changes;
using SmartRail.Api.Modules.Rail;
using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Tests.Access;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Operations;

// The protected driver reads: the assigned journeys of a service date and the briefing for one of
// them. PostgreSQL and real tokens; no driver screens, so this is API evidence only.
public class DriverBriefingApiTests(AccessApi api) : IClassFixture<AccessApi>
{
    // Every test uses its own journey, because they share one database.
    private static int journeysUsed = -1;

    private sealed record Day(Guid JourneyId, DateOnly ServiceDate, List<StopRef> Stops);

    private async Task<Day> NextJourneyAsync()
    {
        var (journeyId, stops) = await api.JourneyWithStopsAsync(Interlocked.Increment(ref journeysUsed));
        var date = default(DateOnly);
        await api.WithDbAsync(async db =>
            date = await db.Journeys.Where(j => j.Id == journeyId).Select(j => j.ServiceDate).SingleAsync());
        return new Day(journeyId, date, stops);
    }

    private static string ListPath(DateOnly date) => $"/api/driver/journeys?serviceDate={date:yyyy-MM-dd}";

    private static string BriefingPath(Guid journeyId) => $"/api/driver/journeys/{journeyId}/briefing";

    private static List<Guid> IdsOf(JsonElement list) =>
        list.GetProperty("journeys").EnumerateArray().Select(j => j.GetProperty("journeyId").GetGuid()).ToList();

    private async Task<int> DeniedAuditCountAsync(Guid actorId)
    {
        var count = 0;
        await api.WithDbAsync(async db =>
            count = await db.AuditEntries.CountAsync(e => e.ActorId == actorId && e.Outcome == "DENIED"));
        return count;
    }

    [RequiresDatabaseFact]
    public async Task An_assigned_driver_lists_only_their_own_journeys_for_the_date()
    {
        var mine = await NextJourneyAsync();
        var someoneElses = await NextJourneyAsync();
        var driver = await api.AddDriverAsync("brief-driver-1", "Sunil Driver", mine.JourneyId);
        var other = await api.AddDriverAsync("brief-other-1", "Other Driver", someoneElses.JourneyId);

        var response = await api.GetAsync("brief-driver-1", ListPath(mine.ServiceDate));
        var list = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mine.ServiceDate.ToString("yyyy-MM-dd"), list.GetProperty("serviceDate").GetString());
        var item = Assert.Single(list.GetProperty("journeys").EnumerateArray(), j => j.GetProperty("journeyId").GetGuid() == mine.JourneyId);
        Assert.DoesNotContain(someoneElses.JourneyId, IdsOf(list));
        Assert.Equal(mine.ServiceDate.ToString("yyyy-MM-dd"), item.GetProperty("serviceDate").GetString());
        Assert.Equal("AS_SCHEDULED", item.GetProperty("availability").GetString());
        Assert.Equal(mine.Stops[0].StationName, item.GetProperty("originName").GetString());
        Assert.Equal(mine.Stops[^1].StationName, item.GetProperty("destinationName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("trainNumber").GetString()));
        Assert.NotNull(response.Headers.ETag);
        Assert.Contains("private", response.Headers.CacheControl!.ToString());

        // The other driver sees theirs, never this one's.
        var otherList = await (await api.GetAsync("brief-other-1", ListPath(someoneElses.ServiceDate))).JsonAsync();
        Assert.Contains(someoneElses.JourneyId, IdsOf(otherList));
        Assert.DoesNotContain(mine.JourneyId, IdsOf(otherList));
        Assert.NotEqual(driver, other);
    }

    [RequiresDatabaseFact]
    public async Task A_driver_with_no_assignment_or_no_journey_that_day_gets_an_empty_list()
    {
        var day = await NextJourneyAsync();
        await api.AddDriverAsync("brief-empty-1", "Idle Driver");
        await api.AddDriverAsync("brief-empty-2", "Busy Elsewhere", day.JourneyId);

        var unassigned = await api.GetAsync("brief-empty-1", ListPath(day.ServiceDate));
        var otherDay = await api.GetAsync("brief-empty-2", ListPath(day.ServiceDate.AddDays(30)));

        Assert.Equal(HttpStatusCode.OK, unassigned.StatusCode);
        Assert.Equal(0, (await unassigned.JsonAsync()).GetProperty("journeys").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, otherDay.StatusCode);
        Assert.Equal(0, (await otherDay.JsonAsync()).GetProperty("journeys").GetArrayLength());
    }

    [RequiresDatabaseFact]
    public async Task A_client_supplied_driver_cannot_select_another_drivers_assignment()
    {
        var mine = await NextJourneyAsync();
        var theirs = await NextJourneyAsync();
        await api.AddDriverAsync("brief-pick-1", "Sunil Driver", mine.JourneyId);
        var other = await api.AddDriverAsync("brief-pick-2", "Other Driver", theirs.JourneyId);

        var list = await (await api.GetAsync("brief-pick-1", $"{ListPath(theirs.ServiceDate)}&driverId={other}")).JsonAsync();

        Assert.DoesNotContain(theirs.JourneyId, IdsOf(list));
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("brief-pick-1", $"{BriefingPath(theirs.JourneyId)}?driverId={other}")).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task The_list_needs_a_valid_date()
    {
        await api.AddDriverAsync("brief-date-1");

        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("brief-date-1", "/api/driver/journeys")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("brief-date-1", "/api/driver/journeys?serviceDate=tomorrow")).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task The_briefing_has_the_dated_stop_ids_and_the_public_schedule_and_thresholds()
    {
        var day = await NextJourneyAsync();
        await api.AddDriverAsync("brief-detail-1", "Sunil Driver", day.JourneyId);

        var response = await api.GetAsync("brief-detail-1", BriefingPath(day.JourneyId));
        var briefing = await response.JsonAsync();
        var pub = await (await api.Client.GetAsync($"/api/journeys/{day.JourneyId}/running")).JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(day.JourneyId, briefing.GetProperty("journeyId").GetGuid());
        Assert.Equal(day.ServiceDate.ToString("yyyy-MM-dd"), briefing.GetProperty("serviceDate").GetString());
        Assert.Equal(pub.GetProperty("trainNumber").GetString(), briefing.GetProperty("trainNumber").GetString());
        Assert.Equal(pub.GetProperty("runningLabel").GetString(), briefing.GetProperty("runningLabel").GetString());
        Assert.Equal(pub.GetProperty("thresholds").GetRawText(), briefing.GetProperty("thresholds").GetRawText());
        Assert.Equal(pub.GetProperty("timetable").GetProperty("label").GetString(), briefing.GetProperty("timetable").GetProperty("label").GetString());
        Assert.Equal(0, briefing.GetProperty("journeyVersion").GetInt32());
        Assert.Equal(0, briefing.GetProperty("changes").GetArrayLength());

        // The stop IDs are the dated stop occurrences, in running order, with the public times.
        var stops = briefing.GetProperty("stops").EnumerateArray().ToList();
        var publicStops = pub.GetProperty("stops").EnumerateArray().ToList();
        Assert.Equal(day.Stops.Select(s => s.StopId), stops.Select(s => s.GetProperty("stopId").GetGuid()));
        Assert.Equal(day.Stops.Select(s => s.StationName), stops.Select(s => s.GetProperty("stationName").GetString()));
        for (var i = 0; i < stops.Count; i++)
        {
            Assert.Equal(publicStops[i].GetProperty("scheduledArrival").GetRawText(), stops[i].GetProperty("scheduledArrival").GetRawText());
            Assert.Equal(publicStops[i].GetProperty("scheduledDeparture").GetRawText(), stops[i].GetProperty("scheduledDeparture").GetRawText());
            Assert.Equal(publicStops[i].GetProperty("plannedPlatform").GetRawText(), stops[i].GetProperty("plannedPlatform").GetRawText());
        }

        // No observation: the evidence is unknown and says so, with nothing to age.
        Assert.Equal("UNKNOWN", briefing.GetProperty("freshness").GetString());
        Assert.Equal(JsonValueKind.Null, briefing.GetProperty("lastObservation").ValueKind);
        Assert.Equal(JsonValueKind.Null, briefing.GetProperty("currentPosition").ValueKind);
        Assert.All(stops, s => Assert.Equal(JsonValueKind.Null, s.GetProperty("estimatedArrival").ValueKind));
        Assert.True(response.Headers.Contains(RunningDetail.ServerTimeHeader));
    }

    [RequiresDatabaseFact]
    public async Task The_briefing_keeps_current_evidence_apart_from_the_timetable()
    {
        var day = await NextJourneyAsync();
        await api.AddDriverAsync("brief-evidence-1", "Sunil Driver", day.JourneyId);
        // To the microsecond, which is all PostgreSQL keeps.
        var observedAt = DateTimeOffset.UtcNow.AddSeconds(-20);
        observedAt = new DateTimeOffset(observedAt.Ticks - observedAt.Ticks % 10, TimeSpan.Zero);
        await api.WithDbAsync(async db =>
        {
            db.Observations.Add(new Observation
            {
                Id = Guid.NewGuid(), JourneyId = day.JourneyId, SourceEventId = $"brief-{Guid.NewGuid():N}",
                FromStopId = day.Stops[0].StopId, ToStopId = day.Stops[1].StopId, ProgressPermille = 400,
                ReportedDelaySeconds = 120, ObservedAt = observedAt, ReceivedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        });

        var briefing = await (await api.GetAsync("brief-evidence-1", BriefingPath(day.JourneyId))).JsonAsync();

        Assert.Equal("CURRENT", briefing.GetProperty("freshness").GetString());
        Assert.Equal(observedAt, briefing.GetProperty("lastObservation").GetProperty("observedAt").GetDateTimeOffset());
        var position = briefing.GetProperty("currentPosition");
        Assert.Equal((day.Stops[0].StopId, day.Stops[1].StopId, 400),
            (position.GetProperty("fromStopId").GetGuid(), position.GetProperty("toStopId").GetGuid(), position.GetProperty("progressPermille").GetInt32()));
        Assert.True(briefing.GetProperty("delaySeconds").GetInt32() >= 120);
        var next = briefing.GetProperty("stops").EnumerateArray().ElementAt(1);
        Assert.NotEqual(JsonValueKind.Null, next.GetProperty("estimatedArrival").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, next.GetProperty("scheduledArrival").ValueKind);
    }

    [RequiresDatabaseFact]
    public async Task Confirmed_changes_show_the_issuer_and_time_while_the_public_read_stays_anonymous()
    {
        var day = await NextJourneyAsync();
        await api.AddDriverAsync("brief-issuer-1", "Sunil Driver", day.JourneyId);
        var controller = await api.AddAccountAsync("brief-controller-1", "Dilani Controller", roles: Roles.Controller);
        var delayId = await api.SeedChangeAsync(day.JourneyId, 1, controller, ChangeDetails.Delay(15));
        var platformId = await api.SeedChangeAsync(day.JourneyId, 2, controller, ChangeDetails.Platform(day.Stops[1].StopId, "5"));
        DateTimeOffset delayAt = default;
        await api.WithDbAsync(async db =>
            delayAt = (await db.ServiceChanges.AsNoTracking().SingleAsync(c => c.Id == delayId)).PublishedAt);

        var response = await api.GetAsync("brief-issuer-1", BriefingPath(day.JourneyId));
        var briefing = await response.JsonAsync();

        Assert.Equal(2, briefing.GetProperty("journeyVersion").GetInt32());
        Assert.Equal(15, briefing.GetProperty("confirmedDelayMinutes").GetInt32());
        var changes = briefing.GetProperty("changes").EnumerateArray().ToList();
        Assert.Equal(2, changes.Count);
        var delay = changes.Single(c => c.GetProperty("changeId").GetGuid() == delayId);
        Assert.Equal(("DELAY", "Dilani Controller", 15), (delay.GetProperty("kind").GetString(), delay.GetProperty("confirmedByName").GetString(), delay.GetProperty("delayMinutes").GetInt32()));
        Assert.Equal(delayAt, delay.GetProperty("confirmedAt").GetDateTimeOffset());
        var platform = changes.Single(c => c.GetProperty("changeId").GetGuid() == platformId);
        Assert.Equal(("PLATFORM", "5", day.Stops[1].StopId), (platform.GetProperty("kind").GetString(), platform.GetProperty("newPlatform").GetString(), platform.GetProperty("targetStopId").GetGuid()));
        var platformStop = briefing.GetProperty("stops").EnumerateArray().Single(s => s.GetProperty("stopId").GetGuid() == day.Stops[1].StopId);
        Assert.Equal("5", platformStop.GetProperty("confirmedPlatform").GetString());

        // Nothing but the confirmed fact: no account ID, audit data or incident link.
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(controller.ToString(), text);
        Assert.DoesNotContain("incident", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audit", text, StringComparison.OrdinalIgnoreCase);

        // The public reads still carry the confirmation time and no name or account.
        var publicText = await (await api.Client.GetAsync($"/api/journeys/{day.JourneyId}/changes")).Content.ReadAsStringAsync();
        Assert.Contains(delayId.ToString(), publicText);
        Assert.DoesNotContain("Dilani", publicText);
        Assert.DoesNotContain(controller.ToString(), publicText);
        Assert.DoesNotContain("confirmedBy", publicText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_issuer_name_is_unavailable_not_invented()
    {
        var change = new ServiceChange(Guid.NewGuid(), 1, ChangeAction.Publish, ChangeDetails.Cancel())
        {
            JourneyId = Guid.NewGuid(), PublishedById = Guid.NewGuid(), PublishedAt = DateTimeOffset.UtcNow,
        };
        var running = new RunningDetail(
            change.JourneyId, new DateOnly(2026, 10, 14), "T1", "FICTIONAL_DEMO", "SmartRail simulation", null,
            "SmartRail simulation", 180, 240, "UNKNOWN", null, null, null, null, null, null, "CANCELLED", null, null, []);

        var dto = DriverBriefingDto.From(running, 1, [change], new Dictionary<Guid, string>());

        Assert.Null(Assert.Single(dto.Changes).ConfirmedByName);
    }

    [RequiresDatabaseFact]
    public async Task A_withdrawn_change_is_not_listed()
    {
        var day = await NextJourneyAsync();
        await api.AddDriverAsync("brief-withdrawn-1", "Sunil Driver", day.JourneyId);
        var controller = await api.AddAccountAsync("brief-controller-2", "Dilani Controller", roles: Roles.Controller);
        var delayId = await api.SeedChangeAsync(day.JourneyId, 1, controller, ChangeDetails.Delay(10));
        await api.SeedChangeAsync(day.JourneyId, 2, controller, new ChangeDetails(ChangeKind.Delay), ChangeAction.Withdraw, delayId);

        var briefing = await (await api.GetAsync("brief-withdrawn-1", BriefingPath(day.JourneyId))).JsonAsync();

        Assert.Equal(0, briefing.GetProperty("changes").GetArrayLength());
        Assert.Equal(2, briefing.GetProperty("journeyVersion").GetInt32());
    }

    [RequiresDatabaseFact]
    public async Task An_unchanged_briefing_is_a_304_with_the_server_clock()
    {
        var day = await NextJourneyAsync();
        await api.AddDriverAsync("brief-etag-1", "Sunil Driver", day.JourneyId);
        var first = await api.GetAsync("brief-etag-1", BriefingPath(day.JourneyId));

        using var client = api.ClientFor(api.Token("brief-etag-1"));
        var request = new HttpRequestMessage(HttpMethod.Get, BriefingPath(day.JourneyId));
        request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(first.Headers.ETag!.ToString()));
        var again = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
        Assert.True(again.Headers.Contains(RunningDetail.ServerTimeHeader));

        // A new confirmed change moves the ETag.
        var controller = await api.AddAccountAsync("brief-controller-3", "Dilani Controller", roles: Roles.Controller);
        await api.SeedChangeAsync(day.JourneyId, 1, controller, ChangeDetails.Delay(5));
        var changed = await api.GetAsync("brief-etag-1", BriefingPath(day.JourneyId));
        Assert.NotEqual(first.Headers.ETag, changed.Headers.ETag);
    }

    [RequiresDatabaseFact]
    public async Task An_unassigned_driver_is_refused_and_an_unknown_journey_is_404()
    {
        var day = await NextJourneyAsync();
        var elsewhere = await NextJourneyAsync();
        var driver = await api.AddDriverAsync("brief-denied-1", "Sunil Driver", elsewhere.JourneyId);

        var refused = await api.GetAsync("brief-denied-1", BriefingPath(day.JourneyId));
        var unknown = await api.GetAsync("brief-denied-1", BriefingPath(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(1, await DeniedAuditCountAsync(driver));
    }

    [RequiresDatabaseFact]
    public async Task Anonymous_and_invalid_tokens_are_401_on_both_reads()
    {
        var day = await NextJourneyAsync();
        await api.AddDriverAsync("brief-anon-1", "Sunil Driver", day.JourneyId);
        var expired = api.Token("brief-anon-1", new AccessApi.TokenOptions { ExpiresIn = TimeSpan.FromMinutes(-5) });

        foreach (var path in new[] { ListPath(day.ServiceDate), BriefingPath(day.JourneyId) })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync(null, path)).StatusCode);
            using var client = api.ClientFor(expired);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        }
    }

    [RequiresDatabaseFact]
    public async Task A_disabled_account_is_refused_on_the_next_request()
    {
        var day = await NextJourneyAsync();
        var driver = await api.AddDriverAsync("brief-disabled-1", "Sunil Driver", day.JourneyId);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("brief-disabled-1", BriefingPath(day.JourneyId))).StatusCode);

        await api.WithDbAsync(db => db.Accounts.Where(a => a.Id == driver)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Status, AccountStatus.Disabled)));

        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("brief-disabled-1", BriefingPath(day.JourneyId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("brief-disabled-1", ListPath(day.ServiceDate))).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task Losing_the_assignment_or_the_permission_removes_access_on_the_next_request()
    {
        var day = await NextJourneyAsync();
        var driver = await api.AddDriverAsync("brief-revoked-1", "Sunil Driver", day.JourneyId);
        Assert.Contains(day.JourneyId, IdsOf(await (await api.GetAsync("brief-revoked-1", ListPath(day.ServiceDate))).JsonAsync()));

        // The assignment goes: the list is empty and the briefing is refused.
        await api.WithDbAsync(db => db.JourneyAssignments.Where(a => a.DriverId == driver).ExecuteDeleteAsync());
        Assert.Equal(0, (await (await api.GetAsync("brief-revoked-1", ListPath(day.ServiceDate))).JsonAsync()).GetProperty("journeys").GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("brief-revoked-1", BriefingPath(day.JourneyId))).StatusCode);

        // The assignment returns, then the Driver role goes: both reads are refused, and audited.
        await api.AssignAsync(driver, day.JourneyId);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("brief-revoked-1", BriefingPath(day.JourneyId))).StatusCode);
        await api.WithDbAsync(db => db.RoleGrants.Where(g => g.AccountId == driver).ExecuteDeleteAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("brief-revoked-1", ListPath(day.ServiceDate))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("brief-revoked-1", BriefingPath(day.JourneyId))).StatusCode);
        Assert.True(await DeniedAuditCountAsync(driver) >= 3);
    }

    [RequiresDatabaseFact]
    public async Task Staff_without_the_driver_role_cannot_read_a_briefing()
    {
        var day = await NextJourneyAsync();
        await api.AddAccountAsync("brief-controller-4", "Dilani Controller", roles: Roles.Controller);
        await api.AddAccountAsync("brief-auditor-1", "Auditor", roles: Roles.Auditor);

        foreach (var subject in new[] { "brief-controller-4", "brief-auditor-1" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync(subject, ListPath(day.ServiceDate))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync(subject, BriefingPath(day.JourneyId))).StatusCode);
        }
    }
}
