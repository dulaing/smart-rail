using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Reports;
using SmartRail.Api.Tests.Access;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Operations;

// A driver sends a field report and reads it back: server receipt is one fact, a controller's
// acknowledgement is another. PostgreSQL, real tokens, no Control screens.
public class DriverReportSubmitApiTests(AccessApi api) : IClassFixture<AccessApi>
{
    private sealed class FailingAudit : IAuditTrail
    {
        public void Stage(HttpContext context, Guid actorId, string action, string outcome, string targetType, Guid? targetId) =>
            throw new InvalidOperationException("audit down");

        public Task<IReadOnlyList<AuditRecord>> ReadForTargetAsync(string targetType, Guid targetId, int limit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("audit down");

        public Task<IReadOnlyList<AuditRecord>> ReadForTargetsAsync(IReadOnlyList<AuditTargets> targets, int limit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("audit down");
    }

    // Every test uses its own journey, because they share one database.
    private static int journeysUsed = -1;

    private Task<Guid> NextJourneyAsync() => api.JourneyIdAsync(Interlocked.Increment(ref journeysUsed));

    // The current time to the microsecond, which is all PostgreSQL keeps.
    private static DateTimeOffset MicrosecondNow()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
    }

    [RequiresDatabaseFact]
    public async Task An_assigned_driver_gets_a_server_receipt_that_is_not_an_acknowledgement()
    {
        var journeyId = await NextJourneyAsync();
        var driver = await api.AddDriverAsync("rep-driver-1", journeyIds: journeyId);
        var key = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var response = await api.SubmitReportAsync("rep-driver-1", journeyId, ReportsApiExtensions.Report(key, "OBSTRUCTION"));
        var body = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/driver/journeys/{journeyId}/reports/{body.GetProperty("reportId").GetGuid()}", response.Headers.Location?.OriginalString);
        Assert.Equal("SENT_AWAITING_CONTROL", body.GetProperty("state").GetString());
        Assert.Equal(key, body.GetProperty("clientReportId").GetGuid());
        Assert.Equal(journeyId, body.GetProperty("journeyId").GetGuid());
        Assert.Equal("OBSTRUCTION", body.GetProperty("reasonCode").GetString());
        Assert.InRange(body.GetProperty("receivedAt").GetDateTimeOffset(), before, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("acknowledgedById").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("acknowledgedByName").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("acknowledgedAt").ValueKind);

        var stored = Assert.Single(await api.ReportsOnAsync(journeyId));
        Assert.Equal((driver, key, "OBSTRUCTION", null, null), (stored.SubmittedById, stored.ClientReportId, stored.ReasonCode, stored.AcknowledgedById, stored.AcknowledgedAt));
        Assert.Equal(body.GetProperty("receivedAt").GetDateTimeOffset(), stored.ReceivedAt);

        var audit = Assert.Single(await api.AuditAsync(driver));
        Assert.Equal(("report.submit", "SUCCEEDED", "REPORT", (Guid?)stored.Id), (audit.Action, audit.Outcome, audit.TargetType, audit.TargetId));
    }

    [RequiresDatabaseFact]
    public async Task A_report_changes_no_journey_state()
    {
        var journeyId = await NextJourneyAsync();
        await api.AddDriverAsync("rep-driver-2", journeyIds: journeyId);
        var changesBefore = (await api.GetAsync(null, $"/api/journeys/{journeyId}/changes")).Headers.ETag;

        await api.SubmitReportAsync("rep-driver-2", journeyId, ReportsApiExtensions.Report(Guid.NewGuid(), "CANNOT_CONTINUE"));

        Assert.Equal((0, 0, 0), await api.CountsAsync(journeyId));
        Assert.Equal(changesBefore, (await api.GetAsync(null, $"/api/journeys/{journeyId}/changes")).Headers.ETag);
    }

    [RequiresDatabaseFact]
    public async Task The_server_decides_the_actor_and_receipt_time()
    {
        var journeyId = await NextJourneyAsync();
        var driver = await api.AddDriverAsync("rep-driver-3", journeyIds: journeyId);
        var other = await api.AddAccountAsync("rep-other-3");

        var response = await api.SubmitReportAsync("rep-driver-3", journeyId, new
        {
            clientReportId = Guid.NewGuid(),
            reasonCode = "TRAIN_FAULT",
            submittedById = other,
            receivedAt = "2020-01-01T00:00:00Z",
            acknowledgedById = other,
            acknowledgedAt = "2020-01-01T00:00:00Z",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = Assert.Single(await api.ReportsOnAsync(journeyId));
        Assert.Equal(driver, stored.SubmittedById);
        Assert.True(stored.ReceivedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Null(stored.AcknowledgedById);
        Assert.Null(stored.AcknowledgedAt);
    }

    [RequiresDatabaseFact]
    public async Task A_lost_response_is_recovered_by_retrying_or_by_looking_up_the_key()
    {
        var journeyId = await NextJourneyAsync();
        var driver = await api.AddDriverAsync("rep-driver-4", journeyIds: journeyId);
        var key = Guid.NewGuid();
        var first = await (await api.SubmitReportAsync("rep-driver-4", journeyId, ReportsApiExtensions.Report(key))).JsonAsync();

        var retry = await api.SubmitReportAsync("rep-driver-4", journeyId, ReportsApiExtensions.Report(key));
        var retried = await retry.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(first.GetProperty("reportId").GetGuid(), retried.GetProperty("reportId").GetGuid());
        Assert.Equal(first.GetProperty("receivedAt").GetDateTimeOffset(), retried.GetProperty("receivedAt").GetDateTimeOffset());
        var report = Assert.Single(await api.ReportsOnAsync(journeyId));
        Assert.Equal(1, await api.ReportAuditCountAsync(report.Id));
        Assert.Single(await api.AuditAsync(driver));

        var lookup = await (await api.GetAsync("rep-driver-4", $"{ReportsApiExtensions.ReportsPath(journeyId)}?clientReportId={key}")).JsonAsync();
        var item = Assert.Single(lookup.GetProperty("items").EnumerateArray());
        Assert.Equal(report.Id, item.GetProperty("reportId").GetGuid());
        Assert.Equal(JsonValueKind.Null, lookup.GetProperty("nextCursor").ValueKind);

        var missing = await (await api.GetAsync("rep-driver-4", $"{ReportsApiExtensions.ReportsPath(journeyId)}?clientReportId={Guid.NewGuid()}")).JsonAsync();
        Assert.Equal(0, missing.GetProperty("items").GetArrayLength());
    }

    [RequiresDatabaseFact]
    public async Task A_retry_after_acknowledgement_returns_the_latest_state_and_the_original_receipt()
    {
        var journeyId = await NextJourneyAsync();
        await api.AddDriverAsync("rep-driver-5", journeyIds: journeyId);
        var controller = await api.AddAccountAsync("rep-controller-5", "Nadeesha Controller", roles: Roles.Controller);
        var key = Guid.NewGuid();
        var first = await (await api.SubmitReportAsync("rep-driver-5", journeyId, ReportsApiExtensions.Report(key))).JsonAsync();
        var ackAt = MicrosecondNow().AddMinutes(1);
        await api.AcknowledgeAsync(first.GetProperty("reportId").GetGuid(), controller, ackAt);

        var retry = await api.SubmitReportAsync("rep-driver-5", journeyId, ReportsApiExtensions.Report(key));
        var body = await retry.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("ACKNOWLEDGED", body.GetProperty("state").GetString());
        Assert.Equal(controller, body.GetProperty("acknowledgedById").GetGuid());
        Assert.Equal("Nadeesha Controller", body.GetProperty("acknowledgedByName").GetString());
        Assert.Equal(ackAt, body.GetProperty("acknowledgedAt").GetDateTimeOffset());
        Assert.Equal(first.GetProperty("receivedAt").GetDateTimeOffset(), body.GetProperty("receivedAt").GetDateTimeOffset());
        Assert.Single(await api.ReportsOnAsync(journeyId));
    }

    [RequiresDatabaseFact]
    public async Task Reusing_a_key_for_different_input_is_a_generic_conflict()
    {
        var firstJourney = await NextJourneyAsync();
        var secondJourney = await NextJourneyAsync();
        await api.AddDriverAsync("rep-driver-6", journeyIds: [firstJourney, secondJourney]);
        var rival = await api.AddDriverAsync("rep-driver-6b", "Rival Driver", firstJourney, secondJourney);
        var key = Guid.NewGuid();
        var mine = await (await api.SubmitReportAsync("rep-driver-6", firstJourney, ReportsApiExtensions.Report(key, "SIGNAL_DELAY"))).JsonAsync();

        var otherReason = await api.SubmitReportAsync("rep-driver-6", firstJourney, ReportsApiExtensions.Report(key, "TRAIN_FAULT"));
        var otherJourney = await api.SubmitReportAsync("rep-driver-6", secondJourney, ReportsApiExtensions.Report(key, "SIGNAL_DELAY"));
        var otherDriver = await api.SubmitReportAsync("rep-driver-6b", firstJourney, ReportsApiExtensions.Report(key, "SIGNAL_DELAY"));

        foreach (var response in new[] { otherReason, otherJourney, otherDriver })
        {
            var text = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("CLIENT_REPORT_ID_IN_USE", text);
            Assert.DoesNotContain(mine.GetProperty("reportId").GetString()!, text);
            Assert.DoesNotContain(firstJourney.ToString(), text);
            Assert.DoesNotContain("Sunil", text);
        }

        Assert.Single(await api.ReportsOnAsync(firstJourney));
        Assert.Empty(await api.ReportsOnAsync(secondJourney));
        Assert.Empty(await api.AuditAsync(rival));
    }

    [RequiresDatabaseFact]
    public async Task Concurrent_identical_submissions_create_one_report_and_one_success_audit()
    {
        for (var round = 0; round < 3; round++)
        {
            var journeyId = await NextJourneyAsync();
            var subject = $"rep-race-{round}";
            var driver = await api.AddDriverAsync(subject, journeyIds: journeyId);
            var key = Guid.NewGuid();

            var responses = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => api.SubmitReportAsync(subject, journeyId, ReportsApiExtensions.Report(key))));

            var statuses = responses.Select(r => r.StatusCode).ToList();
            Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.Created));
            Assert.Equal(7, statuses.Count(s => s == HttpStatusCode.OK));
            var ids = new HashSet<Guid>();
            foreach (var response in responses)
            {
                ids.Add((await response.JsonAsync()).GetProperty("reportId").GetGuid());
            }

            var report = Assert.Single(await api.ReportsOnAsync(journeyId));
            Assert.Equal([report.Id], ids);
            Assert.Equal(1, await api.ReportAuditCountAsync(report.Id));
            Assert.Single(await api.AuditAsync(driver));
        }
    }

    [RequiresDatabaseFact]
    public async Task A_bad_request_is_a_400_and_stores_nothing()
    {
        var journeyId = await NextJourneyAsync();
        var driver = await api.AddDriverAsync("rep-driver-7", journeyIds: journeyId);
        var client = api.ClientFor(api.Token("rep-driver-7"));
        var path = ReportsApiExtensions.ReportsPath(journeyId);

        async Task<HttpStatusCode> PostRawAsync(string json) =>
            (await client.PostAsync(path, new StringContent(json, System.Text.Encoding.UTF8, "application/json"))).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync("{}"));
        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync("""{"reasonCode":"TRAIN_FAULT"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync($$"""{"clientReportId":"{{Guid.Empty}}","reasonCode":"TRAIN_FAULT"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync("""{"clientReportId":"","reasonCode":"TRAIN_FAULT"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync("""{"clientReportId":"not-a-uuid","reasonCode":"TRAIN_FAULT"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync($$"""{"clientReportId":"{{Guid.NewGuid()}}"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync($$"""{"clientReportId":"{{Guid.NewGuid()}}","reasonCode":"train_fault"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync($$"""{"clientReportId":"{{Guid.NewGuid()}}","reasonCode":"FIRE"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, await PostRawAsync("not json"));

        Assert.Empty(await api.ReportsOnAsync(journeyId));
        Assert.Empty(await api.AuditAsync(driver));
    }

    [RequiresDatabaseFact]
    public async Task Only_an_assigned_driver_may_report_and_every_refusal_is_audited()
    {
        var journeyId = await NextJourneyAsync();
        var otherJourney = await NextJourneyAsync();
        var unassigned = await api.AddDriverAsync("rep-unassigned", journeyIds: otherJourney);
        var passenger = await api.AddAccountAsync("rep-passenger", roles: Roles.Passenger);
        var controller = await api.AddAccountAsync("rep-controller-8", roles: Roles.Controller);
        var auditor = await api.AddAccountAsync("rep-auditor", roles: Roles.Auditor);
        var sysAdmin = await api.AddAccountAsync("rep-sysadmin", roles: Roles.SysAdmin);
        var path = ReportsApiExtensions.ReportsPath(journeyId);
        var body = ReportsApiExtensions.Report(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.SubmitReportAsync(null, journeyId, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync(null, path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.ClientFor("not.a.jwt").PostAsJsonAsync(path, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.SubmitReportAsync("rep-nobody-has-this-subject", journeyId, body)).StatusCode);

        foreach (var (subject, actor) in new[]
        {
            ("rep-unassigned", unassigned), ("rep-passenger", passenger), ("rep-controller-8", controller),
            ("rep-auditor", auditor), ("rep-sysadmin", sysAdmin),
        })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await api.SubmitReportAsync(subject, journeyId, body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync(subject, path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync(subject, $"{path}/{Guid.NewGuid()}")).StatusCode);

            var denied = await api.AuditAsync(actor);
            Assert.Equal(3, denied.Count);
            Assert.All(denied, e => Assert.Equal(("DENIED", "JOURNEY", (Guid?)journeyId), (e.Outcome, e.TargetType, e.TargetId)));
            Assert.Equal(
                ["driver.reports.create", "driver.reports.read", "driver.reports.read"],
                denied.Select(e => e.Action).Order().ToList());
        }

        Assert.Empty(await api.ReportsOnAsync(journeyId));
    }

    [RequiresDatabaseFact]
    public async Task An_unknown_journey_is_a_404()
    {
        await api.AddDriverAsync("rep-driver-9");
        var path = ReportsApiExtensions.ReportsPath(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, (await api.SubmitReportAsync("rep-driver-9", Guid.NewGuid(), ReportsApiExtensions.Report(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("rep-driver-9", path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("rep-driver-9", $"{path}/{Guid.NewGuid()}")).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task A_driver_reads_only_their_own_reports()
    {
        var journeyId = await NextJourneyAsync();
        var elsewhere = await NextJourneyAsync();
        await api.AddDriverAsync("rep-driver-10", journeyIds: [journeyId, elsewhere]);
        await api.AddDriverAsync("rep-driver-10b", "Second Driver", journeyId);
        var key = Guid.NewGuid();
        var mine = await (await api.SubmitReportAsync("rep-driver-10", journeyId, ReportsApiExtensions.Report(key))).JsonAsync();
        await api.SubmitReportAsync("rep-driver-10b", journeyId, ReportsApiExtensions.Report(Guid.NewGuid(), "TRAIN_FAULT"));
        var path = ReportsApiExtensions.ReportsPath(journeyId);
        var reportId = mine.GetProperty("reportId").GetGuid();

        var read = await api.GetAsync("rep-driver-10", $"{path}/{reportId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(key, (await read.JsonAsync()).GetProperty("clientReportId").GetGuid());

        var list = await (await api.GetAsync("rep-driver-10", path)).JsonAsync();
        Assert.Equal([reportId], list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("reportId").GetGuid()));

        // Another driver, even on the same journey, gets the same 404 as a report that does not exist,
        // and cannot find it by key either.
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("rep-driver-10b", $"{path}/{reportId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("rep-driver-10b", $"{path}/{Guid.NewGuid()}")).StatusCode);
        var byKey = await (await api.GetAsync("rep-driver-10b", $"{path}?clientReportId={key}")).JsonAsync();
        Assert.Equal(0, byKey.GetProperty("items").GetArrayLength());

        // A report is only found under its own journey, even by its own driver.
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("rep-driver-10", $"{ReportsApiExtensions.ReportsPath(elsewhere)}/{reportId}")).StatusCode);
        var otherList = await (await api.GetAsync("rep-driver-10", ReportsApiExtensions.ReportsPath(elsewhere))).JsonAsync();
        Assert.Equal(0, otherList.GetProperty("items").GetArrayLength());
    }

    [RequiresDatabaseFact]
    public async Task A_driver_list_is_newest_first_in_stable_pages()
    {
        var journeyId = await NextJourneyAsync();
        var driver = await api.AddDriverAsync("rep-driver-11", journeyIds: journeyId);
        var path = ReportsApiExtensions.ReportsPath(journeyId);

        var sent = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var body = await (await api.SubmitReportAsync("rep-driver-11", journeyId, ReportsApiExtensions.Report(Guid.NewGuid()))).JsonAsync();
            sent.Add(body.GetProperty("reportId").GetGuid());
        }

        // Five more with one shared receipt time, so only the ID order separates them.
        var tied = MicrosecondNow().AddHours(-1);
        var tiedIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        await api.WithDbAsync(async db =>
        {
            db.DriverReports.AddRange(tiedIds.Select(id => new DriverReport
            {
                Id = id, JourneyId = journeyId, SubmittedById = driver, ClientReportId = Guid.NewGuid(),
                ReasonCode = "TRAIN_FAULT", ReceivedAt = tied,
            }));
            await db.SaveChangesAsync();
        });

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = $"{path}?limit=3" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await (await api.GetAsync("rep-driver-11", url)).JsonAsync();
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("reportId").GetGuid()));
            cursor = page.GetProperty("nextCursor").GetString();
            pages++;
        }
        while (cursor is not null && pages < 10);

        Assert.Equal(3, pages);
        Assert.Equal(sent.AsEnumerable().Reverse(), seen.Take(3));
        Assert.Equal(tiedIds.Order(), seen.Skip(3).Order());
        Assert.Equal(8, seen.Distinct().Count());

        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("rep-driver-11", $"{path}?cursor=garbage")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("rep-driver-11", $"{path}?clientReportId=nope")).StatusCode);
    }

    [Fact]
    public void Page_sizes_default_to_50_and_stay_between_1_and_100_and_a_cursor_survives_a_round_trip()
    {
        Assert.Equal(50, PageCursor.SizeFor(null));
        Assert.Equal(1, PageCursor.SizeFor(0));
        Assert.Equal(1, PageCursor.SizeFor(-5));
        Assert.Equal(100, PageCursor.SizeFor(100));
        Assert.Equal(100, PageCursor.SizeFor(5000));

        var cursor = new PageCursor(new DateTimeOffset(2026, 10, 17, 4, 5, 6, TimeSpan.Zero).AddTicks(1230), Guid.NewGuid());
        Assert.True(PageCursor.TryDecode(cursor.Encode(), out var decoded));
        Assert.Equal(cursor, decoded);

        foreach (var bad in new[] { "", "garbage", "!!!", "MTIz" })
        {
            Assert.False(PageCursor.TryDecode(bad, out _));
        }
    }

    [RequiresDatabaseFact]
    public async Task Losing_the_grant_the_assignment_or_the_account_stops_access_on_the_next_request()
    {
        var journeyId = await NextJourneyAsync();
        var path = ReportsApiExtensions.ReportsPath(journeyId);
        var key = Guid.NewGuid();

        var noGrant = await api.AddDriverAsync("rep-lose-grant", journeyIds: journeyId);
        var noAssignment = await api.AddDriverAsync("rep-lose-assignment", journeyIds: journeyId);
        var expired = await api.AddDriverAsync("rep-lose-time", journeyIds: journeyId);
        var disabled = await api.AddDriverAsync("rep-lose-account", journeyIds: journeyId);
        var reportIds = new Dictionary<string, Guid>();
        foreach (var subject in new[] { "rep-lose-grant", "rep-lose-assignment", "rep-lose-time", "rep-lose-account" })
        {
            var created = await (await api.SubmitReportAsync(subject, journeyId, ReportsApiExtensions.Report(Guid.NewGuid()))).JsonAsync();
            reportIds[subject] = created.GetProperty("reportId").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await api.GetAsync(subject, $"{path}/{reportIds[subject]}")).StatusCode);
        }

        await api.WithDbAsync(async db =>
        {
            await db.RoleGrants.Where(g => g.AccountId == noGrant).ExecuteDeleteAsync();
            await db.JourneyAssignments.Where(a => a.DriverId == noAssignment).ExecuteDeleteAsync();
            await db.RoleGrants.Where(g => g.AccountId == expired)
                .ExecuteUpdateAsync(set => set.SetProperty(g => g.ValidUntil, DateTimeOffset.UtcNow.AddMinutes(-1)));
            await db.Accounts.Where(a => a.Id == disabled)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.Status, AccountStatus.Disabled));
        });

        foreach (var subject in reportIds.Keys)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync(subject, path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync(subject, $"{path}/{reportIds[subject]}")).StatusCode);
            // A retry of a report already stored is refused too: authorization comes before the replay.
            Assert.Equal(HttpStatusCode.Forbidden, (await api.SubmitReportAsync(subject, journeyId, ReportsApiExtensions.Report(key))).StatusCode);
        }

        Assert.Equal(4, (await api.ReportsOnAsync(journeyId)).Count);
    }

    [RequiresDatabaseFact]
    public async Task A_failing_audit_write_stores_no_report_and_the_same_key_works_afterwards()
    {
        var journeyId = await NextJourneyAsync();
        var driver = await api.AddDriverAsync("rep-fail-audit", journeyIds: journeyId);
        var key = Guid.NewGuid();

        try
        {
            var response = await api.PostWithAsync(
                services => services.Replace(ServiceDescriptor.Scoped<IAuditTrail, FailingAudit>()),
                "rep-fail-audit", ReportsApiExtensions.ReportsPath(journeyId), ReportsApiExtensions.Report(key));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        catch (InvalidOperationException)
        {
            // The test host may surface the failure as an exception instead of a 500.
        }

        Assert.Empty(await api.ReportsOnAsync(journeyId));
        Assert.Empty(await api.AuditAsync(driver));

        var retry = await api.SubmitReportAsync("rep-fail-audit", journeyId, ReportsApiExtensions.Report(key));
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Single(await api.ReportsOnAsync(journeyId));
    }

    [RequiresDatabaseFact]
    public async Task An_auditor_sees_the_submission_in_the_journey_audit_and_a_controller_does_not()
    {
        var journeyId = await NextJourneyAsync();
        await api.AddDriverAsync("rep-driver-12", "Sunil Perera", journeyId);
        await api.AddAccountAsync("rep-auditor-12", roles: Roles.Auditor);
        await api.AddAccountAsync("rep-controller-12", roles: Roles.Controller);
        var created = await (await api.SubmitReportAsync("rep-driver-12", journeyId, ReportsApiExtensions.Report(Guid.NewGuid()))).JsonAsync();

        var audit = await api.GetAsync("rep-auditor-12", $"/api/operations/journeys/{journeyId}/audit");
        var entries = (await audit.JsonAsync()).GetProperty("entries").EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        var entry = Assert.Single(entries, e => e.GetProperty("action").GetString() == "report.submit");
        Assert.Equal("Sunil Perera", entry.GetProperty("actorName").GetString());
        Assert.Equal("REPORT", entry.GetProperty("targetType").GetString());
        Assert.Equal(created.GetProperty("reportId").GetGuid(), entry.GetProperty("targetId").GetGuid());
        Assert.DoesNotContain(created.GetProperty("clientReportId").GetString()!, await audit.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("rep-controller-12", $"/api/operations/journeys/{journeyId}/audit")).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task Reports_survive_migrating_and_reseeding_and_the_driver_keeps_read_access()
    {
        var journeyId = await NextJourneyAsync();
        await api.AddDriverAsync("rep-driver-13", journeyIds: journeyId);
        var created = await (await api.SubmitReportAsync("rep-driver-13", journeyId, ReportsApiExtensions.Report(Guid.NewGuid()))).JsonAsync();

        await api.MigrateAndSeedAsync();
        await api.MigrateAndSeedAsync();

        var read = await api.GetAsync("rep-driver-13", $"{ReportsApiExtensions.ReportsPath(journeyId)}/{created.GetProperty("reportId").GetGuid()}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Single(await api.ReportsOnAsync(journeyId));
    }

    [RequiresDatabaseFact]
    public async Task The_database_itself_refuses_a_second_use_of_a_key_a_half_acknowledgement_and_an_unknown_reason()
    {
        var journeyId = await NextJourneyAsync();
        var driver = await api.AddDriverAsync("rep-driver-14", journeyIds: journeyId);
        var key = Guid.NewGuid();

        DriverReport New(Guid clientId, string reason = "TRAIN_FAULT", Guid? by = null, DateTimeOffset? at = null) => new()
        {
            Id = Guid.NewGuid(), JourneyId = journeyId, SubmittedById = driver, ClientReportId = clientId,
            ReasonCode = reason, ReceivedAt = DateTimeOffset.UtcNow, AcknowledgedById = by, AcknowledgedAt = at,
        };

        async Task SaveAsync(DriverReport report) => await api.WithDbAsync(async db =>
        {
            db.DriverReports.Add(report);
            await db.SaveChangesAsync();
        });

        await SaveAsync(New(key));
        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(New(key)));
        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(New(Guid.NewGuid(), "FIRE")));
        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(New(Guid.NewGuid(), by: driver)));
        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(New(Guid.NewGuid(), at: DateTimeOffset.UtcNow)));
        await SaveAsync(New(Guid.NewGuid(), by: driver, at: DateTimeOffset.UtcNow));
    }
}
