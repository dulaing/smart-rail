using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Tests.Access;
using SmartRail.Api.Tests.Rail;
using Xunit.Abstractions;

namespace SmartRail.Api.Tests.Operations;

// The feature acceptance drill from _specs/driver-reports.md, run through the real API pipeline
// (token validation, authorization, PostgreSQL, audit) with the seeded roles. It proves the API's
// behaviour only: that a device shows "Not sent" before delivery cannot be tested here.
public class DriverReportsAcceptanceTests(AccessApi api, ITestOutputHelper output) : IClassFixture<AccessApi>
{
    private void Step(string text) => output.WriteLine(text);

    [RequiresDatabaseFact]
    public async Task A_report_travels_from_the_driver_to_Control_to_an_incident_to_a_withdrawn_change()
    {
        var journeyId = await api.JourneyIdAsync();
        var driver = await api.AddAccountAsync("drill-driver", "Sunil Perera", roles: Roles.Driver);
        await api.AssignAsync(driver, journeyId);
        await api.AddAccountAsync("drill-unassigned-driver", "Kamal Silva", roles: Roles.Driver);
        var controller = await api.AddAccountAsync("drill-controller", "Nadeesha Fernando", roles: Roles.Controller);
        await api.AddAccountAsync("drill-auditor", "Auditor Ama", roles: Roles.Auditor);
        await api.AddAccountAsync("drill-sysadmin", "Admin Ravi", roles: Roles.SysAdmin);
        var reports = ReportsApiExtensions.ReportsPath(journeyId);
        var key = Guid.NewGuid();

        // 1. The assigned driver submits; the response is lost; the same key is retried.
        var sent = await api.SubmitReportAsync("drill-driver", journeyId, ReportsApiExtensions.Report(key, "SIGNAL_DELAY"));
        var sentBody = await sent.JsonAsync();
        var reportId = sentBody.GetProperty("reportId").GetGuid();
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        var retried = await api.SubmitReportAsync("drill-driver", journeyId, ReportsApiExtensions.Report(key, "SIGNAL_DELAY"));
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(reportId, (await retried.JsonAsync()).GetProperty("reportId").GetGuid());
        var recovered = await (await api.GetAsync("drill-driver", $"{reports}?clientReportId={key}")).JsonAsync();
        Assert.Equal(reportId, recovered.GetProperty("items")[0].GetProperty("reportId").GetGuid());
        Step("1. POST report -> 201; retry with the same clientReportId -> 200 and the same report; lookup by key finds it");

        // 2. The driver sees server receipt, awaiting Control, with no acknowledgement.
        var waiting = await (await api.GetAsync("drill-driver", $"{reports}/{reportId}")).JsonAsync();
        Assert.Equal("SENT_AWAITING_CONTROL", waiting.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, waiting.GetProperty("acknowledgedById").ValueKind);
        var inbox = await (await api.GetAsync("drill-controller", $"/api/operations/reports?journeyId={journeyId}&acknowledged=false")).JsonAsync();
        Assert.Equal(reportId, inbox.GetProperty("items")[0].GetProperty("reportId").GetGuid());
        Step("2. Driver read: SENT_AWAITING_CONTROL; Control inbox (acknowledged=false) lists it");

        // 3. Control acknowledges; the driver now reads the named controller and the time.
        var acknowledged = await api.PostEmptyAsync("drill-controller", $"/api/operations/reports/{reportId}/acknowledge");
        Assert.Equal(HttpStatusCode.OK, acknowledged.StatusCode);
        var named = await (await api.GetAsync("drill-driver", $"{reports}/{reportId}")).JsonAsync();
        Assert.Equal("ACKNOWLEDGED", named.GetProperty("state").GetString());
        Assert.Equal(controller, named.GetProperty("acknowledgedById").GetGuid());
        Assert.Equal("Nadeesha Fernando", named.GetProperty("acknowledgedByName").GetString());
        Assert.Equal(waiting.GetProperty("receivedAt").GetDateTimeOffset(), named.GetProperty("receivedAt").GetDateTimeOffset());
        Step("3. POST acknowledge -> 200; driver read: ACKNOWLEDGED by Nadeesha Fernando at the recorded time, receipt time unchanged");

        // 4. Control opens an incident from the report.
        var opened = await api.PostAsync("drill-controller", "/api/operations/incidents", new { sourceReportId = reportId });
        var incidentId = (await opened.JsonAsync()).GetProperty("incidentId").GetGuid();
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        Step("4. POST incident {sourceReportId} -> 201");

        // 5. A delay is previewed and published with the incident link.
        var delay = ChangeBodies.Delay(15).WithIncident(incidentId);
        var preview = await (await api.PreviewAsync("drill-controller", journeyId, delay)).JsonAsync();
        Assert.Equal(incidentId, preview.GetProperty("incidentId").GetGuid());
        var confirmed = await api.ConfirmAsync("drill-controller", journeyId, delay.WithVersion(preview.GetProperty("previewedVersion").GetInt32()));
        var changeId = (await confirmed.JsonAsync()).GetProperty("changeId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Step("5. Preview shows incidentId; confirm -> 200 and a linked 15-minute delay");

        // 6. Resolving the incident leaves the delay in force.
        Assert.Equal(HttpStatusCode.OK, (await api.PostEmptyAsync("drill-controller", $"/api/operations/incidents/{incidentId}/resolve")).StatusCode);
        var stillDelayed = await (await api.GetAsync(null, $"/api/journeys/{journeyId}/changes")).JsonAsync();
        Assert.Equal(15, stillDelayed.GetProperty("confirmedDelayMinutes").GetInt32());
        var incident = await (await api.GetAsync("drill-controller", $"/api/operations/incidents/{incidentId}")).JsonAsync();
        Assert.Equal("RESOLVED", incident.GetProperty("state").GetString());
        Assert.Equal([changeId], incident.GetProperty("changeIds").EnumerateArray().Select(e => e.GetGuid()));
        var stillNamed = await (await api.GetAsync("drill-driver", $"{reports}/{reportId}")).JsonAsync();
        Assert.Equal("ACKNOWLEDGED", stillNamed.GetProperty("state").GetString());
        Step("6. POST resolve -> 200; the public journey still shows the 15-minute delay; the report is still acknowledged");

        // 7. The delay is withdrawn explicitly, as its own previewed decision.
        var withdrawal = ChangeBodies.Withdraw(changeId);
        var withdrawPreview = await (await api.PreviewAsync("drill-controller", journeyId, withdrawal)).JsonAsync();
        Assert.Equal(incidentId, withdrawPreview.GetProperty("incidentId").GetGuid());
        var withdrawn = await api.ConfirmAsync("drill-controller", journeyId, withdrawal.WithVersion(withdrawPreview.GetProperty("previewedVersion").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        var cleared = await (await api.GetAsync(null, $"/api/journeys/{journeyId}/changes")).JsonAsync();
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("confirmedDelayMinutes").ValueKind);
        Step("7. Preview then confirm withdrawal -> 200, keeps the resolved incident's link; the delay is gone");

        // 8. The auditor reads the history, in order, with names.
        var trail = (await (await api.GetAsync("drill-auditor", $"/api/operations/journeys/{journeyId}/audit")).JsonAsync())
            .GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(
            ["report.submit", "report.acknowledge", "incident.open", "change.publish.delay", "incident.resolve", "change.withdraw.delay"],
            trail.Select(e => e.GetProperty("action").GetString()));
        Assert.Equal(
            ["Sunil Perera", "Nadeesha Fernando", "Nadeesha Fernando", "Nadeesha Fernando", "Nadeesha Fernando", "Nadeesha Fernando"],
            trail.Select(e => e.GetProperty("actorName").GetString()));
        Assert.All(trail, e => Assert.Equal("SUCCEEDED", e.GetProperty("outcome").GetString()));
        var incidentTrail = (await (await api.GetAsync("drill-auditor", $"/api/operations/incidents/{incidentId}/audit")).JsonAsync())
            .GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("action").GetString());
        Assert.Equal(["incident.open", "incident.resolve"], incidentTrail);
        Step("8. Auditor reads the journey trail (6 entries, in order, with names) and the incident trail (open, resolve)");

        // 9. A SysAdmin and an unassigned driver are refused every operational action.
        var before = await api.CountsAsync(journeyId);
        var deniedIncident = new { reasonCode = "TRAIN_FAULT" };
        foreach (var subject in new[] { "drill-sysadmin", "drill-unassigned-driver" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await api.SubmitReportAsync(subject, journeyId, ReportsApiExtensions.Report(Guid.NewGuid()))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync(subject, reports)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await api.PostEmptyAsync(subject, $"/api/operations/reports/{reportId}/acknowledge")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await api.PostAsync(subject, "/api/operations/incidents", deniedIncident)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await api.PostEmptyAsync(subject, $"/api/operations/incidents/{incidentId}/resolve")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await api.ConfirmAsync(subject, journeyId, ChangeBodies.Delay(1).WithIncident(incidentId).WithVersion(before.Version))).StatusCode);
        }

        Assert.Equal(before, await api.CountsAsync(journeyId));
        await api.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.DriverReports.CountAsync(r => r.JourneyId == journeyId));
            Assert.Equal(1, await db.Incidents.CountAsync(i => i.SourceReportId == reportId));
            Assert.Equal(0, await db.AuditEntries.CountAsync(e => e.Outcome == "SUCCEEDED" && e.ActorId != driver && e.ActorId != controller));
        });
        Step("9. SysAdmin and an unassigned driver: 403 on submit, read, acknowledge, open, resolve and linked publish; no row changed");
    }
}
