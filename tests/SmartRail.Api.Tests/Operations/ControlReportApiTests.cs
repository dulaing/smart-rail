using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SmartRail.Api.Infrastructure;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Modules.Operations.Reports;
using SmartRail.Api.Tests.Access;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Operations;

// Control reads the reports and explicitly acknowledges them. Receipt and acknowledgement stay
// two different facts, and the first controller to acknowledge keeps the credit. PostgreSQL,
// real tokens, no Control screens.
public class ControlReportApiTests(AccessApi api) : IClassFixture<AccessApi>
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

    // Every test uses its own journeys, because they share one database.
    private static int journeysUsed = -1;

    private Task<Guid> NextJourneyAsync() => api.JourneyIdAsync(Interlocked.Increment(ref journeysUsed));

    private static string Ack(Guid reportId) => $"/api/operations/reports/{reportId}/acknowledge";

    private static string Inbox(Guid journeyId, string more = "") => $"/api/operations/reports?journeyId={journeyId}{more}";

    private Task<HttpResponseMessage> AcknowledgeAsync(string? subject, Guid reportId) =>
        api.ClientFor(subject is null ? null : api.Token(subject)).PostAsync(Ack(reportId), null);

    // A driver assigned to a new journey who has sent one report. Returns the report's ID.
    private async Task<(Guid JourneyId, Guid ReportId, Guid DriverId)> ReportedAsync(string driverSubject, string reason = "SIGNAL_DELAY")
    {
        var journeyId = await NextJourneyAsync();
        var driver = await api.AddDriverAsync(driverSubject, "Sunil Perera", journeyId);
        var created = await (await api.SubmitReportAsync(driverSubject, journeyId, ReportsApiExtensions.Report(Guid.NewGuid(), reason))).JsonAsync();
        return (journeyId, created.GetProperty("reportId").GetGuid(), driver);
    }

    [RequiresDatabaseFact]
    public async Task Receipt_and_acknowledgement_are_separate_and_the_driver_sees_the_name_and_time()
    {
        var (journeyId, reportId, driver) = await ReportedAsync("ctl-driver-1", "TRAIN_FAULT");
        var controller = await api.AddAccountAsync("ctl-controller-1", "Nadeesha Fernando", roles: Roles.Controller);

        // Sent and received, but nobody at Control has acted.
        var waiting = await (await api.GetAsync("ctl-controller-1", $"/api/operations/reports/{reportId}")).JsonAsync();
        Assert.Equal("SENT_AWAITING_CONTROL", waiting.GetProperty("state").GetString());
        Assert.Equal(driver, waiting.GetProperty("submittedById").GetGuid());
        Assert.Equal("Sunil Perera", waiting.GetProperty("submittedByName").GetString());
        Assert.Equal(JsonValueKind.Null, waiting.GetProperty("acknowledgedById").ValueKind);
        Assert.False(waiting.TryGetProperty("clientReportId", out _));
        var driverView = await (await api.GetAsync("ctl-driver-1", $"{ReportsApiExtensions.ReportsPath(journeyId)}/{reportId}")).JsonAsync();
        Assert.Equal("SENT_AWAITING_CONTROL", driverView.GetProperty("state").GetString());

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var response = await AcknowledgeAsync("ctl-controller-1", reportId);
        var acknowledged = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ACKNOWLEDGED", acknowledged.GetProperty("state").GetString());
        Assert.Equal(controller, acknowledged.GetProperty("acknowledgedById").GetGuid());
        Assert.Equal("Nadeesha Fernando", acknowledged.GetProperty("acknowledgedByName").GetString());
        var ackAt = acknowledged.GetProperty("acknowledgedAt").GetDateTimeOffset();
        Assert.InRange(ackAt, before, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(waiting.GetProperty("receivedAt").GetDateTimeOffset(), acknowledged.GetProperty("receivedAt").GetDateTimeOffset());
        Assert.True(ackAt >= acknowledged.GetProperty("receivedAt").GetDateTimeOffset());

        // The owning driver now reads the named controller and the time, and the receipt is unchanged.
        var seen = await (await api.GetAsync("ctl-driver-1", $"{ReportsApiExtensions.ReportsPath(journeyId)}/{reportId}")).JsonAsync();
        Assert.Equal("ACKNOWLEDGED", seen.GetProperty("state").GetString());
        Assert.Equal(controller, seen.GetProperty("acknowledgedById").GetGuid());
        Assert.Equal("Nadeesha Fernando", seen.GetProperty("acknowledgedByName").GetString());
        Assert.Equal(ackAt, seen.GetProperty("acknowledgedAt").GetDateTimeOffset());
        Assert.Equal(waiting.GetProperty("receivedAt").GetDateTimeOffset(), seen.GetProperty("receivedAt").GetDateTimeOffset());

        var audit = Assert.Single(await api.AuditAsync(controller));
        Assert.Equal(("report.acknowledge", "SUCCEEDED", "REPORT", (Guid?)reportId), (audit.Action, audit.Outcome, audit.TargetType, audit.TargetId));

        // Nothing else happened: no change, no new journey version, no Catering message.
        Assert.Equal((0, 0, 0), await api.CountsAsync(journeyId));
    }

    [RequiresDatabaseFact]
    public async Task A_repeated_or_late_acknowledgement_keeps_the_first_controllers_name_and_time()
    {
        var (_, reportId, _) = await ReportedAsync("ctl-driver-2");
        var first = await api.AddAccountAsync("ctl-first", "First Controller", roles: Roles.Controller);
        var second = await api.AddAccountAsync("ctl-second", "Second Controller", roles: Roles.Controller);

        var win = await (await AcknowledgeAsync("ctl-first", reportId)).JsonAsync();
        var repeat = await AcknowledgeAsync("ctl-first", reportId);
        var late = await AcknowledgeAsync("ctl-second", reportId);

        foreach (var response in new[] { repeat, late })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.JsonAsync();
            Assert.Equal(first, body.GetProperty("acknowledgedById").GetGuid());
            Assert.Equal("First Controller", body.GetProperty("acknowledgedByName").GetString());
            Assert.Equal(win.GetProperty("acknowledgedAt").GetDateTimeOffset(), body.GetProperty("acknowledgedAt").GetDateTimeOffset());
        }

        Assert.Equal(1, await api.ReportAuditCountAsync(reportId, "report.acknowledge"));
        Assert.Single(await api.AuditAsync(first));
        Assert.Empty(await api.AuditAsync(second));
    }

    [RequiresDatabaseFact]
    public async Task Two_controllers_racing_produce_one_winner_everyone_agrees_on()
    {
        var controllers = new Dictionary<string, Guid>
        {
            ["ctl-race-a"] = await api.AddAccountAsync("ctl-race-a", "Racer A", roles: Roles.Controller),
            ["ctl-race-b"] = await api.AddAccountAsync("ctl-race-b", "Racer B", roles: Roles.Controller),
        };

        for (var round = 0; round < 6; round++)
        {
            var (_, reportId, _) = await ReportedAsync($"ctl-race-driver-{round}");

            var responses = await Task.WhenAll(
                Enumerable.Range(0, 6).Select(i => AcknowledgeAsync(i % 2 == 0 ? "ctl-race-a" : "ctl-race-b", reportId)));

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            var bodies = new List<JsonElement>();
            foreach (var response in responses)
            {
                bodies.Add(await response.JsonAsync());
            }

            var winners = bodies.Select(b => (b.GetProperty("acknowledgedById").GetGuid(), b.GetProperty("acknowledgedAt").GetDateTimeOffset())).Distinct().ToList();
            var winner = Assert.Single(winners);
            Assert.Contains(winner.Item1, controllers.Values);

            DriverReport stored = null!;
            await api.WithDbAsync(async db => stored = await db.DriverReports.AsNoTracking().SingleAsync(r => r.Id == reportId));
            Assert.Equal(winner, (stored.AcknowledgedById!.Value, stored.AcknowledgedAt!.Value));

            Assert.Equal(1, await api.ReportAuditCountAsync(reportId, "report.acknowledge"));
            Assert.Single(await api.AuditAsync(winner.Item1), e => e.TargetId == reportId);
        }
    }

    [RequiresDatabaseFact]
    public async Task A_driver_who_retries_after_the_acknowledgement_gets_the_named_state_and_the_original_receipt()
    {
        var journeyId = await NextJourneyAsync();
        await api.AddDriverAsync("ctl-driver-3", journeyIds: journeyId);
        await api.AddAccountAsync("ctl-controller-3", "Named Controller", roles: Roles.Controller);
        var key = Guid.NewGuid();
        var first = await (await api.SubmitReportAsync("ctl-driver-3", journeyId, ReportsApiExtensions.Report(key))).JsonAsync();
        var reportId = first.GetProperty("reportId").GetGuid();

        // The acknowledgement's response is lost; asking again returns the same answer.
        var ack = await (await AcknowledgeAsync("ctl-controller-3", reportId)).JsonAsync();
        var replayed = await (await AcknowledgeAsync("ctl-controller-3", reportId)).JsonAsync();
        Assert.Equal(ack.GetProperty("acknowledgedAt").GetDateTimeOffset(), replayed.GetProperty("acknowledgedAt").GetDateTimeOffset());

        var retry = await api.SubmitReportAsync("ctl-driver-3", journeyId, ReportsApiExtensions.Report(key));
        var body = await retry.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("ACKNOWLEDGED", body.GetProperty("state").GetString());
        Assert.Equal("Named Controller", body.GetProperty("acknowledgedByName").GetString());
        Assert.Equal(ack.GetProperty("acknowledgedAt").GetDateTimeOffset(), body.GetProperty("acknowledgedAt").GetDateTimeOffset());
        Assert.Equal(first.GetProperty("receivedAt").GetDateTimeOffset(), body.GetProperty("receivedAt").GetDateTimeOffset());
        Assert.Single(await api.ReportsOnAsync(journeyId));
        Assert.Equal(1, await api.ReportAuditCountAsync(reportId, "report.submit"));
        Assert.Equal(1, await api.ReportAuditCountAsync(reportId, "report.acknowledge"));
    }

    [RequiresDatabaseFact]
    public async Task The_inbox_is_newest_first_in_stable_filtered_pages()
    {
        var journeyId = await NextJourneyAsync();
        var otherJourney = await NextJourneyAsync();
        var driver = await api.AddDriverAsync("ctl-driver-4", "Sunil Perera", journeyId, otherJourney);
        await api.AddAccountAsync("ctl-controller-4", roles: Roles.Controller);

        var sent = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var body = await (await api.SubmitReportAsync("ctl-driver-4", journeyId, ReportsApiExtensions.Report(Guid.NewGuid()))).JsonAsync();
            sent.Add(body.GetProperty("reportId").GetGuid());
        }

        var elsewhere = await (await api.SubmitReportAsync("ctl-driver-4", otherJourney, ReportsApiExtensions.Report(Guid.NewGuid()))).JsonAsync();

        // Four more with one receipt time, so only the ID order separates them.
        var now = DateTimeOffset.UtcNow;
        var tied = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero).AddHours(-2);
        var tiedIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();
        await api.WithDbAsync(async db =>
        {
            db.DriverReports.AddRange(tiedIds.Select(id => new DriverReport
            {
                Id = id, JourneyId = journeyId, SubmittedById = driver, ClientReportId = Guid.NewGuid(),
                ReasonCode = "OBSTRUCTION", ReceivedAt = tied,
            }));
            await db.SaveChangesAsync();
        });

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = Inbox(journeyId, "&limit=3" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}"));
            var page = await (await api.GetAsync("ctl-controller-4", url)).JsonAsync();
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("reportId").GetGuid()));
            cursor = page.GetProperty("nextCursor").GetString();
            pages++;
        }
        while (cursor is not null && pages < 10);

        Assert.Equal(3, pages);
        Assert.Equal(sent.AsEnumerable().Reverse(), seen.Take(3));
        Assert.Equal(tiedIds.Order(), seen.Skip(3).Order());
        Assert.Equal(7, seen.Distinct().Count());
        Assert.DoesNotContain(elsewhere.GetProperty("reportId").GetGuid(), seen);

        // Unfiltered, the other journey's report is the newest of all.
        var everything = await (await api.GetAsync("ctl-controller-4", "/api/operations/reports?limit=1")).JsonAsync();
        Assert.Equal(
            elsewhere.GetProperty("reportId").GetGuid(),
            everything.GetProperty("items")[0].GetProperty("reportId").GetGuid());
        Assert.NotNull(everything.GetProperty("nextCursor").GetString());

        // The acknowledged filter separates the work still waiting from the work done.
        await AcknowledgeAsync("ctl-controller-4", sent[1]);
        var waiting = await (await api.GetAsync("ctl-controller-4", Inbox(journeyId, "&acknowledged=false"))).JsonAsync();
        var done = await (await api.GetAsync("ctl-controller-4", Inbox(journeyId, "&acknowledged=true"))).JsonAsync();
        Assert.Equal(6, waiting.GetProperty("items").GetArrayLength());
        Assert.Equal([sent[1]], done.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("reportId").GetGuid()));
        Assert.All(waiting.GetProperty("items").EnumerateArray(), i => Assert.Equal("SENT_AWAITING_CONTROL", i.GetProperty("state").GetString()));
        Assert.Equal(7, (await (await api.GetAsync("ctl-controller-4", Inbox(journeyId))).JsonAsync()).GetProperty("items").GetArrayLength());

        // A page size outside 1 to 100 is clamped, not refused.
        Assert.Equal(1, (await (await api.GetAsync("ctl-controller-4", Inbox(journeyId, "&limit=0"))).JsonAsync()).GetProperty("items").GetArrayLength());
        Assert.Equal(7, (await (await api.GetAsync("ctl-controller-4", Inbox(journeyId, "&limit=100000"))).JsonAsync()).GetProperty("items").GetArrayLength());

        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("ctl-controller-4", "/api/operations/reports?cursor=garbage")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("ctl-controller-4", "/api/operations/reports?acknowledged=maybe")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("ctl-controller-4", "/api/operations/reports?journeyId=nope")).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task Only_controllers_use_the_inbox_and_every_refusal_is_audited_and_changes_nothing()
    {
        var (journeyId, reportId, driver) = await ReportedAsync("ctl-driver-5");
        var otherDriver = await api.AddDriverAsync("ctl-driver-5b", "Another Driver", journeyId);
        var auditor = await api.AddAccountAsync("ctl-auditor", roles: Roles.Auditor);
        var sysAdmin = await api.AddAccountAsync("ctl-sysadmin", roles: Roles.SysAdmin);
        var passenger = await api.AddAccountAsync("ctl-passenger", roles: Roles.Passenger);
        var simOperator = await api.AddAccountAsync("ctl-sim", roles: Roles.SimOperator);
        var staff = await api.AddAccountAsync("ctl-staff");
        await api.GrantAsync(staff, Roles.StationStaff, await api.StationIdAsync("GPH"));
        var paths = new[] { "/api/operations/reports", $"/api/operations/reports/{reportId}" };

        foreach (var path in paths)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync(null, path)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await AcknowledgeAsync(null, reportId)).StatusCode);

        foreach (var (subject, actor) in new[]
        {
            ("ctl-driver-5", driver), ("ctl-driver-5b", otherDriver), ("ctl-auditor", auditor), ("ctl-sysadmin", sysAdmin),
            ("ctl-passenger", passenger), ("ctl-sim", simOperator), ("ctl-staff", staff),
        })
        {
            foreach (var path in paths)
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync(subject, path)).StatusCode);
            }

            Assert.Equal(HttpStatusCode.Forbidden, (await AcknowledgeAsync(subject, reportId)).StatusCode);

            var denied = (await api.AuditAsync(actor)).Where(e => e.Outcome == "DENIED").ToList();
            Assert.Equal(3, denied.Count);
            Assert.All(denied, e => Assert.Equal("incidents.manage", e.Action));
        }

        // Nobody was credited, so it still waits for a controller.
        DriverReport stored = null!;
        await api.WithDbAsync(async db => stored = await db.DriverReports.AsNoTracking().SingleAsync(r => r.Id == reportId));
        Assert.Null(stored.AcknowledgedAt);
        Assert.Equal(0, await api.ReportAuditCountAsync(reportId, "report.acknowledge"));
    }

    [RequiresDatabaseFact]
    public async Task A_controller_who_loses_the_grant_or_the_account_is_refused_on_the_next_request()
    {
        var (_, reportId, _) = await ReportedAsync("ctl-driver-6");
        var expired = await api.AddAccountAsync("ctl-expiring", roles: Roles.Controller);
        var disabled = await api.AddAccountAsync("ctl-disabled", roles: Roles.Controller);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("ctl-expiring", $"/api/operations/reports/{reportId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("ctl-disabled", $"/api/operations/reports/{reportId}")).StatusCode);

        await api.WithDbAsync(async db =>
        {
            await db.RoleGrants.Where(g => g.AccountId == expired)
                .ExecuteUpdateAsync(set => set.SetProperty(g => g.ValidUntil, DateTimeOffset.UtcNow.AddMinutes(-1)));
            await db.Accounts.Where(a => a.Id == disabled)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.Status, AccountStatus.Disabled));
        });

        foreach (var subject in new[] { "ctl-expiring", "ctl-disabled" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync(subject, "/api/operations/reports")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await AcknowledgeAsync(subject, reportId)).StatusCode);
        }

        Assert.Equal(0, await api.ReportAuditCountAsync(reportId, "report.acknowledge"));
    }

    [RequiresDatabaseFact]
    public async Task An_unknown_report_is_a_404_and_writes_no_audit_row()
    {
        var controller = await api.AddAccountAsync("ctl-controller-7", roles: Roles.Controller);
        var missing = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("ctl-controller-7", $"/api/operations/reports/{missing}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AcknowledgeAsync("ctl-controller-7", missing)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor(api.Token("ctl-controller-7")).PostAsync("/api/operations/reports/not-a-guid/acknowledge", null)).StatusCode);

        Assert.Empty(await api.AuditAsync(controller));
    }

    [RequiresDatabaseFact]
    public async Task A_failing_audit_write_leaves_the_report_unacknowledged_and_the_next_try_succeeds()
    {
        var (_, reportId, _) = await ReportedAsync("ctl-driver-8");
        var controller = await api.AddAccountAsync("ctl-fail-audit", roles: Roles.Controller);

        try
        {
            var response = await api.PostWithAsync(
                services => services.Replace(ServiceDescriptor.Scoped<IAuditTrail, FailingAudit>()),
                "ctl-fail-audit", Ack(reportId), new { });
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        catch (InvalidOperationException)
        {
            // The test host may surface the failure as an exception instead of a 500.
        }

        DriverReport stored = null!;
        await api.WithDbAsync(async db => stored = await db.DriverReports.AsNoTracking().SingleAsync(r => r.Id == reportId));
        Assert.Null(stored.AcknowledgedById);
        Assert.Null(stored.AcknowledgedAt);
        Assert.Empty(await api.AuditAsync(controller));

        var retry = await AcknowledgeAsync("ctl-fail-audit", reportId);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("ACKNOWLEDGED", (await retry.JsonAsync()).GetProperty("state").GetString());
        Assert.Single(await api.AuditAsync(controller));
    }

    [RequiresDatabaseFact]
    public async Task An_auditor_sees_the_acknowledgement_in_the_journey_audit_in_order()
    {
        var (journeyId, reportId, _) = await ReportedAsync("ctl-driver-9");
        await api.AddAccountAsync("ctl-controller-9", "Nadeesha Fernando", roles: Roles.Controller);
        await api.AddAccountAsync("ctl-auditor-9", roles: Roles.Auditor);
        await AcknowledgeAsync("ctl-controller-9", reportId);
        await AcknowledgeAsync("ctl-controller-9", reportId);

        var audit = await api.GetAsync("ctl-auditor-9", $"/api/operations/journeys/{journeyId}/audit");
        var entries = (await audit.JsonAsync()).GetProperty("entries").EnumerateArray().ToList();

        Assert.Equal(["report.submit", "report.acknowledge"], entries.Select(e => e.GetProperty("action").GetString()));
        Assert.Equal("Nadeesha Fernando", entries[1].GetProperty("actorName").GetString());
        Assert.All(entries, e => Assert.Equal(reportId, e.GetProperty("targetId").GetGuid()));
        Assert.DoesNotContain("clientReportId", await audit.Content.ReadAsStringAsync());
    }

    [RequiresDatabaseFact]
    public async Task The_database_makes_the_first_acknowledgement_final_and_the_report_otherwise_unchangeable()
    {
        var (_, reportId, driver) = await ReportedAsync("ctl-driver-10");
        var first = await api.AddAccountAsync("ctl-db-first", roles: Roles.Controller);
        var second = await api.AddAccountAsync("ctl-db-second", roles: Roles.Controller);

        async Task<PostgresException> RefusedAsync(Func<SmartRailDbContext, Task<int>> update)
        {
            PostgresException? refused = null;
            await api.WithDbAsync(async db =>
            {
                var exception = await Assert.ThrowsAnyAsync<Exception>(() => update(db));
                refused = exception as PostgresException ?? exception.InnerException as PostgresException;
            });
            return Assert.IsType<PostgresException>(refused);
        }

        var at = DateTimeOffset.UtcNow.AddMinutes(1);
        await api.AcknowledgeAsync(reportId, first, at);

        Assert.Contains("final", (await RefusedAsync(db => db.DriverReports.Where(r => r.Id == reportId)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.AcknowledgedById, second)))).MessageText);
        Assert.Contains("final", (await RefusedAsync(db => db.DriverReports.Where(r => r.Id == reportId)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.AcknowledgedAt, at.AddHours(1))))).MessageText);
        Assert.Contains("final", (await RefusedAsync(db => db.DriverReports.Where(r => r.Id == reportId)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.AcknowledgedById, (Guid?)null).SetProperty(r => r.AcknowledgedAt, (DateTimeOffset?)null)))).MessageText);
        Assert.Contains("only the acknowledgement", (await RefusedAsync(db => db.DriverReports.Where(r => r.Id == reportId)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.SubmittedById, second)))).MessageText);
        Assert.Contains("only the acknowledgement", (await RefusedAsync(db => db.DriverReports.Where(r => r.Id == reportId)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.ReasonCode, "OBSTRUCTION")))).MessageText);

        DriverReport stored = null!;
        await api.WithDbAsync(async db => stored = await db.DriverReports.AsNoTracking().SingleAsync(r => r.Id == reportId));
        Assert.Equal((driver, "SIGNAL_DELAY", first), (stored.SubmittedById, stored.ReasonCode, stored.AcknowledgedById!.Value));
    }
}
