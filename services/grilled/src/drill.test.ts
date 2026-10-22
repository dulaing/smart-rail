import assert from "node:assert/strict";
import { createServer, type IncomingMessage } from "node:http";
import type { AddressInfo } from "node:net";
import { afterEach, beforeEach, describe, test } from "node:test";
import { InMemorySpanExporter, SimpleSpanProcessor } from "@opentelemetry/sdk-trace-base";
import { deliverDueCallbacks } from "./callbacks";
import { call, ETA, EVIDENCE, kiosk, lookup, newLink, place, placement, peradeniyaPickup, snapshot } from "./order-fixtures";
import { serviceHeaders, startApp, temporaryDatabasePath, type TestApp } from "./test-support";
import { startTelemetry } from "./telemetry";

// Tracing is installed once for this file (each test file runs in its own process).
const spans = new InMemorySpanExporter();
startTelemetry({ env: {}, spanProcessors: [new SimpleSpanProcessor(spans)] });

const ADMIN_KEY = "test-admin-key";
let database: ReturnType<typeof temporaryDatabasePath>;
let app: TestApp;

async function start(options: { dev?: boolean; adminKey?: string; overrides?: object } = {}) {
  database = temporaryDatabasePath();
  app = await startApp({
    databasePath: database.path,
    ...(options.dev ? { dev: { outage: false } } : {}),
    overrides: { adminKey: options.adminKey, ...options.overrides },
  });
}

afterEach(async () => {
  await app.close();
  database.remove();
  spans.reset();
});

// key = null sends no credential at all.
const admin = (path: string, body: unknown, key: string | null = ADMIN_KEY) =>
  call(app, "PUT", path, { headers: key ? { "X-Grilled-Admin-Key": key } : {}, body });

describe("the provider-owned cutoff", () => {
  beforeEach(() => start({ adminKey: ADMIN_KEY }));

  const path = "/v1/admin/collection-points/peradeniya-junction/cutoff";

  test("is changed with the admin credential and is live for the next order, with no other change", async () => {
    app.setNow("2026-10-18T06:45:00.000Z"); // 40 minutes before the 07:25 ETA
    const pickup = peradeniyaPickup({ etaEvidenceAt: "2026-10-18T06:44:00.000Z" });
    assert.equal((await place(app, newLink(), placement({ pickup }))).status, 201, "open at the default 30 minutes");

    const changed = await admin(path, { cutoffMinutes: 45 });
    assert.deepEqual(changed.body, { id: "peradeniya-junction", cutoffMinutes: 45 });
    const read = await call(app, "GET", "/v1/collection-points/peradeniya-junction", { headers: serviceHeaders() });
    assert.equal(read.body.cutoffMinutes, 45);
    assert.equal(read.headers.get("cache-control"), "no-store");

    const closed = await place(app, newLink(), placement({ pickup }));
    assert.equal(closed.status, 422);
    assert.equal(closed.body.code, "CUTOFF_PASSED");
  });

  test("an order accepted earlier keeps its place and can still be collected", async () => {
    const order = (await place(app, newLink())).body;
    await admin(path, { cutoffMinutes: 45 });
    assert.equal((await lookup(app, order.orderLinkId)).body.status, "ACCEPTED");
    assert.equal((await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/ready`)).status, 200);
  });

  test("needs its own credential: neither the service key nor a kiosk key nor none will do", async () => {
    assert.equal((await admin(path, { cutoffMinutes: 45 }, null)).status, 401);
    assert.equal((await admin(path, { cutoffMinutes: 45 }, "test-service-key")).status, 401);
    assert.equal((await admin(path, { cutoffMinutes: 45 }, "kiosk-key-peradeniya")).status, 401);
    const read = await call(app, "GET", "/v1/collection-points/peradeniya-junction", { headers: serviceHeaders() });
    assert.equal(read.body.cutoffMinutes, 30);
  });

  test("refuses a missing collection point and an out-of-range value", async () => {
    assert.equal((await admin("/v1/admin/collection-points/nowhere/cutoff", { cutoffMinutes: 45 })).status, 404);
    for (const body of [{ cutoffMinutes: -1 }, { cutoffMinutes: 241 }, { cutoffMinutes: 4.5 }, { cutoffMinutes: "45" }, {}]) {
      assert.equal((await admin(path, body)).status, 400, JSON.stringify(body));
    }
  });

  test("fails closed when no admin key is configured", async () => {
    await app.close();
    database.remove();
    await start({});
    assert.equal((await admin(path, { cutoffMinutes: 45 }, "anything")).status, 401);
  });
});

describe("readiness", () => {
  beforeEach(() => start());

  test("is public and reports a healthy database", async () => {
    const res = await call(app, "GET", "/health/ready");
    assert.equal(res.status, 200);
    assert.deepEqual(res.body, { status: "Healthy", checks: { database: "Healthy", migrations: "Healthy" } });
  });

  test("is not ready when its tables are behind this build", async () => {
    app.db.exec("DELETE FROM schema_migrations WHERE id = (SELECT max(id) FROM schema_migrations)");
    const res = await call(app, "GET", "/health/ready");
    assert.equal(res.status, 503);
    assert.equal(res.body.checks.migrations, "Behind");
  });

  test("liveness stays independent of it", async () => {
    app.db.exec("DELETE FROM schema_migrations");
    assert.equal((await call(app, "GET", "/health/live")).status, 200);
  });
});

describe("the simulated outage switch", () => {
  test("does not exist unless the process was started with dev controls", async () => {
    await start();
    assert.equal((await call(app, "PUT", "/internal/dev/outage", { headers: serviceHeaders(), body: { active: true } })).status, 404);
  });

  test("needs the service credential and a boolean", async () => {
    await start({ dev: true });
    assert.equal((await call(app, "PUT", "/internal/dev/outage", { body: { active: true } })).status, 401);
    assert.equal((await call(app, "GET", "/internal/dev/outage")).status, 401);
    assert.equal((await call(app, "PUT", "/internal/dev/outage", { headers: serviceHeaders(), body: { active: "yes" } })).status, 400);
    assert.deepEqual((await call(app, "GET", "/internal/dev/outage", { headers: serviceHeaders() })).body, { outage: false });
  });

  test("answers 503 on every provider route, keeps health and the restore route reachable, then restores", async () => {
    await start({ dev: true });
    const order = (await place(app, newLink())).body;

    const on = await call(app, "PUT", "/internal/dev/outage", { headers: serviceHeaders(), body: { active: true } });
    assert.deepEqual(on.body, { outage: true });

    for (const [method, path, headers] of [
      ["GET", "/v1/collection-points", serviceHeaders()],
      ["GET", `/v1/order-links/${order.orderLinkId}`, serviceHeaders()],
      ["POST", "/v1/journey-events", serviceHeaders()],
      ["POST", `/v1/kiosk/collection-points/peradeniya-junction/orders/${order.id}/ready`, { "X-Grilled-Kiosk-Key": "kiosk-key-peradeniya" }],
    ] as const) {
      const res = await call(app, method, path, { headers });
      assert.equal(res.status, 503, `${method} ${path}`);
      assert.equal(res.body.code, "SIMULATED_OUTAGE");
    }
    assert.equal((await call(app, "GET", "/health/live")).status, 200, "the process is alive");
    const ready = await call(app, "GET", "/health/ready");
    assert.equal(ready.status, 503);
    assert.deepEqual(ready.body, { status: "Unhealthy", checks: { simulatedOutage: "Active" } });

    // The restore route works while everything else is down, and nothing was lost.
    const off = await call(app, "PUT", "/internal/dev/outage", { headers: serviceHeaders(), body: { active: false } });
    assert.deepEqual(off.body, { outage: false });
    assert.equal((await lookup(app, order.orderLinkId)).status, 200);
    assert.equal((await call(app, "GET", "/health/ready")).status, 200);
  });

  test("holds queued callbacks while it lasts and sends them afterwards", async () => {
    await start({ dev: true });
    await place(app, newLink());
    const received: string[] = [];
    const smartRail = createServer((req: IncomingMessage, res) => {
      req.resume();
      req.on("end", () => {
        received.push(String(req.headers["x-grilled-event-id"]));
        res.writeHead(200, { "Content-Type": "application/json" });
        res.end('{"result":"APPLIED"}');
      });
    });
    await new Promise<void>((resolve) => smartRail.listen(0, resolve));
    const config = { ...app.config, smartRailBaseUrl: `http://127.0.0.1:${(smartRail.address() as AddressInfo).port}`, callbackSecret: "s" };
    try {
      const now = new Date("2026-10-18T05:00:00.000Z");
      assert.deepEqual(await deliverDueCallbacks(app.db, config, now, fetch, () => true), { attempted: 0, delivered: 0 });
      assert.equal(received.length, 0);
      assert.deepEqual(await deliverDueCallbacks(app.db, config, now, fetch, () => false), { attempted: 1, delivered: 1 });
      assert.equal(received.length, 1);
    } finally {
      await new Promise<void>((resolve) => {
        smartRail.close(() => resolve());
        smartRail.closeAllConnections();
      });
    }
  });
});

describe("trace correlation", () => {
  const TRACE_ID = "4bf92f3577b34da6a3ce929d0e0e4736";
  const PARENT = `00-${TRACE_ID}-00f067aa0ba902b7-01`;

  beforeEach(() => start());

  test("a request continues its caller's trace, and the span is named by route, never by id", async () => {
    const link = newLink();
    await call(app, "PUT", `/v1/order-links/${link}`, {
      headers: { ...serviceHeaders(), traceparent: PARENT },
      body: placement(),
    });

    const server = spans.getFinishedSpans().find((s) => s.attributes["http.route"] === "/v1/order-links/:orderLinkId");
    assert.ok(server, "a server span was recorded");
    assert.equal(server.spanContext().traceId, TRACE_ID);
    assert.equal(server.parentSpanContext?.spanId, "00f067aa0ba902b7");
    assert.equal(server.name, "PUT /v1/order-links/:orderLinkId");
    assert.equal(server.attributes["http.response.status_code"], 201);
    assert.ok(!JSON.stringify(server.attributes).includes(link), "the link id is not in the span");
  });

  test("the callback an action causes carries the same trace to SmartRail", async () => {
    const order = (await call(app, "PUT", `/v1/order-links/${newLink()}`, { headers: { ...serviceHeaders(), traceparent: PARENT }, body: placement() })).body;
    const row = app.db.prepare("SELECT traceparent FROM webhook_outbox").get() as { traceparent: string };
    assert.equal(row.traceparent.split("-")[1], TRACE_ID, "stored with the status change");

    const seen: string[] = [];
    const smartRail = createServer((req, res) => {
      req.resume();
      req.on("end", () => {
        seen.push(String(req.headers["traceparent"]));
        res.writeHead(200, { "Content-Type": "application/json" });
        res.end('{"result":"APPLIED"}');
      });
    });
    await new Promise<void>((resolve) => smartRail.listen(0, resolve));
    try {
      const config = { ...app.config, smartRailBaseUrl: `http://127.0.0.1:${(smartRail.address() as AddressInfo).port}`, callbackSecret: "s" };
      await deliverDueCallbacks(app.db, config, new Date("2026-10-18T05:00:00.000Z"));
    } finally {
      await new Promise<void>((resolve) => {
        smartRail.close(() => resolve());
        smartRail.closeAllConnections();
      });
    }

    assert.equal(seen.length, 1);
    assert.equal(seen[0]!.split("-")[1], TRACE_ID, "SmartRail receives the callback inside the original trace");
    const delivery = spans.getFinishedSpans().find((s) => s.name === "grilled.callback.deliver");
    assert.equal(delivery?.spanContext().traceId, TRACE_ID);
    assert.equal(delivery?.attributes["grilled.callback.result"], "delivered");
    assert.ok(order.id);
  });

  test("a kiosk action's callback carries the kiosk request's trace", async () => {
    const order = (await place(app, newLink())).body;
    const kioskTrace = "0af7651916cd43dd8448eb211c80319c";
    await call(app, "POST", `/v1/kiosk/collection-points/peradeniya-junction/orders/${order.id}/ready`, {
      headers: { "X-Grilled-Kiosk-Key": "kiosk-key-peradeniya", traceparent: `00-${kioskTrace}-b7ad6b7169203331-01` },
    });
    const rows = app.db.prepare("SELECT status_sequence, traceparent FROM webhook_outbox ORDER BY status_sequence").all() as { status_sequence: number; traceparent: string | null }[];
    assert.equal(rows.length, 2);
    assert.equal(rows[1]!.traceparent?.split("-")[1], kioskTrace);
  });

  test("a train-change event joins the trace of the delivery that carried it", async () => {
    await place(app, newLink());
    await call(app, "POST", "/v1/journey-events", {
      headers: { ...serviceHeaders(), traceparent: PARENT },
      body: {
        eventId: "train-change:t6",
        eventType: "DELAY",
        journey: snapshot({ version: 2 }),
        stops: [peradeniyaPickup({ estimatedArrival: "2026-10-18T07:45:00.000Z", etaEvidenceAt: EVIDENCE })],
        calculatedAt: "2026-10-18T05:00:00.000Z",
        occurredAt: "2026-10-18T05:00:00.000Z",
      },
    });
    const rows = app.db.prepare("SELECT status_sequence, traceparent FROM webhook_outbox ORDER BY status_sequence").all() as { status_sequence: number; traceparent: string }[];
    assert.equal(rows.length, 2);
    assert.equal(rows[1]!.traceparent.split("-")[1], TRACE_ID, "the re-timing callback belongs to the event's trace");
    assert.notEqual(ETA, "2026-10-18T07:45:00.000Z");
  });
});
