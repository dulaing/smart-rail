using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartRail.Api.Modules.Access;
using SmartRail.Api.Tests.Access;
using SmartRail.Api.Tests.Rail;

namespace SmartRail.Api.Tests.Operations;

// The incident and journey audit trails page oldest-first with a cursor, so a history longer than
// one bounded page stays readable and a later action never becomes inaccessible. PostgreSQL, real
// tokens, no screens.
public class AuditPaginationApiTests(AccessApi api) : IClassFixture<AccessApi>
{
    private const string Incidents = "/api/operations/incidents";

    private static AuditEntry Entry(string targetType, Guid targetId, Guid actorId, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        ActorId = actorId,
        Action = "audit.seed",
        Outcome = AuditOutcomes.Succeeded,
        TargetType = targetType,
        TargetId = targetId,
        CorrelationId = Guid.NewGuid().ToString("N"),
        OccurredAt = at,
    };

    // count entries a second apart, plus three sharing the oldest instant, so the trail is longer
    // than the largest page (200) and exercises the (occurredAt, id) tie-break. Times are aligned
    // to whole microseconds so a value read back equals the one written.
    private async Task<DateTimeOffset> SeedAsync(string targetType, Guid targetId, Guid actorId, int count)
    {
        var day = DateTimeOffset.UtcNow.AddDays(-1);
        var baseTime = new DateTimeOffset(day.Ticks - day.Ticks % 10, TimeSpan.Zero);
        var seeded = new List<AuditEntry>();
        for (var i = 0; i < 3; i++)
        {
            seeded.Add(Entry(targetType, targetId, actorId, baseTime));
        }

        for (var i = 0; i < count; i++)
        {
            seeded.Add(Entry(targetType, targetId, actorId, baseTime.AddSeconds(1 + i)));
        }

        await api.WithDbAsync(async db =>
        {
            db.AuditEntries.AddRange(seeded);
            await db.SaveChangesAsync();
        });
        return baseTime;
    }

    // Follows nextCursor to the end, checking that complete and nextCursor agree on every page.
    private async Task<List<(Guid Id, DateTimeOffset OccurredAt)>> TraverseAsync(string subject, string path, int limit)
    {
        var seen = new List<(Guid, DateTimeOffset)>();
        string? cursor = null;
        for (var page = 0; ; page++)
        {
            Assert.True(page < 50, "the traversal did not reach the end of the history");
            var query = $"?limit={limit}" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var body = await (await api.GetAsync(subject, path + query)).JsonAsync();
            seen.AddRange(body.GetProperty("entries").EnumerateArray()
                .Select(e => (e.GetProperty("id").GetGuid(), e.GetProperty("occurredAt").GetDateTimeOffset())));
            cursor = body.GetProperty("nextCursor").GetString();
            if (cursor is null)
            {
                Assert.True(body.GetProperty("complete").GetBoolean(), "a page with no next cursor must be complete");
                return seen;
            }

            Assert.False(body.GetProperty("complete").GetBoolean(), "a page with a next cursor is not complete");
        }
    }

    private async Task<List<Guid>> StoredAuditIdsAsync(string targetType, Guid targetId)
    {
        var ids = new List<Guid>();
        await api.WithDbAsync(async db =>
            ids = await db.AuditEntries.AsNoTracking()
                .Where(e => e.TargetType == targetType && e.TargetId == targetId)
                .Select(e => e.Id).ToListAsync());
        return ids;
    }

    [RequiresDatabaseFact]
    public async Task An_incident_history_longer_than_a_page_traverses_stably_without_omission()
    {
        var actor = await api.AddAccountAsync("audit-inc-auditor", "Ada Auditor", roles: Roles.Auditor);
        await api.AddAccountAsync("audit-inc-controller", roles: Roles.Controller);
        var incidentId = (await (await api.PostAsync("audit-inc-controller", Incidents, new { reasonCode = "TRAIN_FAULT" })).JsonAsync())
            .GetProperty("incidentId").GetGuid();
        var path = $"{Incidents}/{incidentId}/audit";
        var tied = await SeedAsync("INCIDENT", incidentId, actor, 205);

        // A default read is one bounded page and never pretends to be the whole history.
        var first = await (await api.GetAsync("audit-inc-auditor", path)).JsonAsync();
        Assert.Equal(100, first.GetProperty("entries").GetArrayLength());
        Assert.False(first.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.String, first.GetProperty("nextCursor").ValueKind);

        var pages = await TraverseAsync("audit-inc-auditor", path, 200);
        var seen = pages.Select(p => p.Id).ToList();
        var stored = await StoredAuditIdsAsync("INCIDENT", incidentId);
        Assert.True(seen.Count > 200);
        Assert.Equal(stored.Count, seen.Count);
        Assert.Equal(stored.Order(), seen.Order());

        // Stable (occurredAt, id) order: time never goes backwards and the tied entries stay together.
        Assert.Equal(pages.Select(p => p.OccurredAt).Order(), pages.Select(p => p.OccurredAt));
        var tiedIndices = pages.Select((p, i) => (p, i)).Where(x => x.p.OccurredAt == tied).Select(x => x.i).ToList();
        Assert.Equal(3, tiedIndices.Count);
        Assert.Equal(Enumerable.Range(tiedIndices[0], tiedIndices.Count), tiedIndices);

        // The same traversal twice gives the same order: the cursor is deterministic.
        var again = (await TraverseAsync("audit-inc-auditor", path, 200)).Select(p => p.Id).ToList();
        Assert.Equal(seen, again);
    }

    [RequiresDatabaseFact]
    public async Task A_later_audit_action_stays_reachable_past_a_full_first_page()
    {
        var actor = await api.AddAccountAsync("audit-late-auditor", "Ada Auditor", roles: Roles.Auditor);
        await api.AddAccountAsync("audit-late-controller", roles: Roles.Controller);
        var incidentId = (await (await api.PostAsync("audit-late-controller", Incidents, new { reasonCode = "SIGNAL_DELAY" })).JsonAsync())
            .GetProperty("incidentId").GetGuid();
        var path = $"{Incidents}/{incidentId}/audit";
        await SeedAsync("INCIDENT", incidentId, actor, 205);

        // Read only the oldest page, then write an entry newer than everything recorded.
        var first = await (await api.GetAsync("audit-late-auditor", path)).JsonAsync();
        Assert.False(first.GetProperty("complete").GetBoolean());
        var lateId = Guid.NewGuid();
        await api.WithDbAsync(async db =>
        {
            db.AuditEntries.Add(new AuditEntry
            {
                Id = lateId,
                ActorId = actor,
                Action = "incident.resolve",
                Outcome = AuditOutcomes.Succeeded,
                TargetType = "INCIDENT",
                TargetId = incidentId,
                CorrelationId = Guid.NewGuid().ToString("N"),
                OccurredAt = DateTimeOffset.UtcNow.AddMinutes(5),
            });
            await db.SaveChangesAsync();
        });

        var pages = await TraverseAsync("audit-late-auditor", path, 200);
        Assert.Contains(lateId, pages.Select(p => p.Id));
        Assert.Equal(lateId, pages[^1].Id);
    }

    [RequiresDatabaseFact]
    public async Task A_journey_history_pages_with_a_cursor_and_keeps_the_audit_permission()
    {
        var actor = await api.AddAccountAsync("audit-j-auditor", "Ada Auditor", roles: Roles.Auditor);
        await api.AddAccountAsync("audit-j-controller", roles: Roles.Controller);
        await api.AddAccountAsync("audit-j-passenger", roles: Roles.Passenger);
        var journeyId = await api.JourneyIdAsync(0);
        var path = $"/api/operations/journeys/{journeyId}/audit";
        await SeedAsync("JOURNEY", journeyId, actor, 205);

        var pages = await TraverseAsync("audit-j-auditor", path, 200);
        Assert.True(pages.Count > 200);
        var stored = await StoredAuditIdsAsync("JOURNEY", journeyId);
        Assert.Equal(stored.Count, pages.Count);
        Assert.Equal(stored.Order(), pages.Select(p => p.Id).Order());

        var first = await (await api.GetAsync("audit-j-auditor", $"{path}?limit=200")).JsonAsync();
        Assert.False(first.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.String, first.GetProperty("nextCursor").ValueKind);

        // Authorization is unchanged by pagination, and a bad cursor is refused.
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync(null, path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("audit-j-controller", path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("audit-j-passenger", path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("audit-j-auditor", $"/api/operations/journeys/{Guid.NewGuid()}/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("audit-j-auditor", $"{path}?cursor=garbage")).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task An_incident_audit_page_keeps_its_permission_and_refuses_a_bad_cursor()
    {
        await api.AddAccountAsync("audit-auth-controller", roles: Roles.Controller);
        await api.AddAccountAsync("audit-auth-auditor", roles: Roles.Auditor);
        await api.AddAccountAsync("audit-auth-passenger", roles: Roles.Passenger);
        var incidentId = (await (await api.PostAsync("audit-auth-controller", Incidents, new { reasonCode = "OBSTRUCTION" })).JsonAsync())
            .GetProperty("incidentId").GetGuid();
        var path = $"{Incidents}/{incidentId}/audit";
        var wrongShape = Convert.ToBase64String(Encoding.UTF8.GetBytes("not-a-cursor")).TrimEnd('=');

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync(null, path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("audit-auth-controller", path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("audit-auth-passenger", path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("audit-auth-auditor", path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("audit-auth-auditor", $"{Incidents}/{Guid.NewGuid()}/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("audit-auth-auditor", $"{path}?cursor=not-a-token")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("audit-auth-auditor", $"{path}?cursor={wrongShape}")).StatusCode);
    }
}
