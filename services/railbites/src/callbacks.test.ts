import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createServer, type IncomingMessage, type Server } from "node:http";
import type { AddressInfo } from "node:net";
import { join } from "node:path";
import { afterEach, beforeEach, describe, test } from "node:test";
import { deliverDueCallbacks } from "./callbacks";
import { call, kiosk, newLink, place, placePolgahawela, stockOf } from "./order-fixtures";
import { sign, verify } from "./signature";
import { startApp, temporaryDatabasePath, type TestApp } from "./test-support";

const SECRET = "test-callback-secret";

interface Received {
  headers: IncomingMessage["headers"];
  body: string;
}

// A stand-in for SmartRail's receiver that records what it is sent and answers as told.
class FakeSmartRail {
  readonly received: Received[] = [];
  status = 200;
  result = "APPLIED";
  dropAfterReading = false;
  private server: Server;

  constructor() {
    this.server = createServer((req, res) => {
      const chunks: Buffer[] = [];
      req.on("data", (chunk: Buffer) => chunks.push(chunk));
      req.on("end", () => {
        this.received.push({ headers: req.headers, body: Buffer.concat(chunks).toString("utf8") });
        if (this.dropAfterReading) {
          req.socket.destroy();
          return;
        }
        res.writeHead(this.status, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ result: this.result }));
      });
    });
  }

  async start(): Promise<string> {
    await new Promise<void>((resolve) => this.server.listen(0, resolve));
    return `http://127.0.0.1:${(this.server.address() as AddressInfo).port}`;
  }

  async stop(): Promise<void> {
    await new Promise<void>((resolve) => {
      this.server.close(() => resolve());
      this.server.closeAllConnections();
    });
  }
}

let database: ReturnType<typeof temporaryDatabasePath>;
let app: TestApp;
let smartRail: FakeSmartRail;
let url: string;

const NOW = new Date("2026-10-18T05:00:00.000Z");
const at = (seconds: number) => new Date(NOW.getTime() + seconds * 1000);

beforeEach(async () => {
  smartRail = new FakeSmartRail();
  url = await smartRail.start();
  database = temporaryDatabasePath();
  app = await startApp({
    databasePath: database.path,
    overrides: { smartRailBaseUrl: url, callbackSecret: SECRET, callbackMaxBackoffSeconds: 8 },
  });
});

afterEach(async () => {
  await app.close();
  await smartRail.stop();
  database.remove();
});

const rows = () =>
  app.db.prepare("SELECT event_id, payload, attempt_count, next_attempt_at, delivered_at, last_error FROM webhook_outbox ORDER BY created_at, rowid").all() as unknown as {
    event_id: string;
    payload: string;
    attempt_count: number;
    next_attempt_at: string;
    delivered_at: string | null;
    last_error: string | null;
  }[];

const pass = (when: Date) => deliverDueCallbacks(app.db, app.config, when);

describe("signing", () => {
  const fixtures = JSON.parse(readFileSync(join(__dirname, "..", "fixtures", "signed-callbacks.json"), "utf8")) as {
    secret: string;
    cases: { name: string; timestamp: number; body: string; signature: string; accepted: boolean }[];
  };

  for (const fixture of fixtures.cases) {
    test(`fixture: ${fixture.name}`, () => {
      const validlySigned = verify(fixtures.secret, fixture.timestamp, fixture.body, fixture.signature);
      if (fixture.accepted) {
        assert.ok(validlySigned, "an accepted fixture must carry a correct signature");
      }
      // Only the two time-based refusals are signed correctly; every other refusal has a bad signature.
      const refusedByTimeOnly = /expired|too far ahead/.test(fixture.name);
      assert.equal(validlySigned, fixture.accepted || refusedByTimeOnly);
    });
  }

  test("the signature covers the timestamp and the exact bytes of the body", () => {
    const body = '{"a":1}';
    assert.notEqual(sign("s", 1, body), sign("s", 2, body));
    assert.notEqual(sign("s", 1, body), sign("s", 1, '{"a": 1}'));
    assert.notEqual(sign("s", 1, body), sign("t", 1, body));
    assert.match(sign("s", 1, body), /^v1=[0-9a-f]{64}$/);
  });
});

describe("delivery", () => {
  test("a queued callback is sent signed over its exact body, then recorded as delivered", async () => {
    const link = newLink();
    const order = (await place(app, link)).body;

    const result = await pass(NOW);

    assert.deepEqual(result, { attempted: 1, delivered: 1 });
    const [sent] = smartRail.received;
    assert.equal(sent!.body, rows()[0]!.payload);
    assert.equal(sent!.headers["x-railbites-timestamp"], String(NOW.getTime() / 1000));
    assert.ok(verify(SECRET, NOW.getTime() / 1000, sent!.body, sent!.headers["x-railbites-signature"] as string));
    assert.equal(sent!.headers["x-railbites-event-id"], `${order.id}-1`);
    assert.ok(!sent!.body.includes(order.pickupCode), "the pickup code is never sent");
    assert.equal(rows()[0]!.delivered_at, NOW.toISOString());
    assert.equal(rows()[0]!.attempt_count, 1);
  });

  test("a transient failure is retried with the same event and body, a fresh signature and growing, capped delays", async () => {
    await place(app, newLink());
    smartRail.status = 503;

    assert.deepEqual(await pass(NOW), { attempted: 1, delivered: 0 });
    assert.equal(rows()[0]!.next_attempt_at, at(2).toISOString());
    assert.equal(rows()[0]!.delivered_at, null);

    assert.deepEqual(await pass(at(1)), { attempted: 0, delivered: 0 }); // not due yet

    await pass(at(2));
    assert.equal(rows()[0]!.next_attempt_at, at(2 + 4).toISOString());
    await pass(at(6));
    assert.equal(rows()[0]!.next_attempt_at, at(6 + 8).toISOString(), "capped at 8 seconds in this test");
    await pass(at(14));
    assert.equal(rows()[0]!.next_attempt_at, at(14 + 8).toISOString());

    smartRail.status = 200;
    assert.deepEqual(await pass(at(22)), { attempted: 1, delivered: 1 });

    const bodies = new Set(smartRail.received.map((r) => r.body));
    const events = new Set(smartRail.received.map((r) => r.headers["x-railbites-event-id"]));
    const stamps = smartRail.received.map((r) => r.headers["x-railbites-timestamp"]);
    assert.equal(smartRail.received.length, 5);
    assert.equal(bodies.size, 1, "the body never changes between retries");
    assert.equal(events.size, 1);
    assert.equal(new Set(stamps).size, 5, "each retry is signed with a fresh timestamp");
    for (const received of smartRail.received) {
      const stamp = Number(received.headers["x-railbites-timestamp"]);
      assert.ok(verify(SECRET, stamp, received.body, received.headers["x-railbites-signature"] as string));
    }
  });

  test("an unreachable SmartRail is retried the same way", async () => {
    await place(app, newLink());
    await smartRail.stop();
    assert.deepEqual(await pass(NOW), { attempted: 1, delivered: 0 });
    assert.equal(rows()[0]!.last_error, "UNREACHABLE");
    assert.equal(rows()[0]!.delivered_at, null);
    smartRail = new FakeSmartRail(); // so afterEach has something to stop
  });

  test("only a committed receipt acknowledges delivery; an arbitrary 2xx or unknown result is retried", async () => {
    await place(app, newLink());
    smartRail.status = 202;
    assert.deepEqual(await pass(NOW), { attempted: 1, delivered: 0 });
    assert.equal(rows()[0]!.delivered_at, null);
    smartRail.status = 200;
    smartRail.result = "PENDING";
    assert.deepEqual(await pass(at(2)), { attempted: 1, delivered: 0 });
    assert.equal(rows()[0]!.last_error, "INVALID_ACKNOWLEDGEMENT");
    smartRail.result = "DUPLICATE";
    assert.deepEqual(await pass(at(6)), { attempted: 1, delivered: 1 });
  });

  test("a permanent refusal is kept and retried slowly, so correcting the cause lets it through", async () => {
    await place(app, newLink());
    smartRail.status = 401;

    await pass(NOW);
    assert.equal(rows()[0]!.last_error, "HTTP_401");
    assert.equal(rows()[0]!.next_attempt_at, at(600).toISOString());
    assert.equal(rows()[0]!.delivered_at, null);
    assert.deepEqual(await pass(at(300)), { attempted: 0, delivered: 0 });

    smartRail.status = 200;
    assert.deepEqual(await pass(at(600)), { attempted: 1, delivered: 1 });
    assert.equal(rows()[0]!.last_error, null);
  });

  test("a crash after SmartRail committed but before it answered redelivers the identical callback", async () => {
    await place(app, newLink());
    smartRail.dropAfterReading = true;

    assert.deepEqual(await pass(NOW), { attempted: 1, delivered: 0 });
    assert.equal(smartRail.received.length, 1, "SmartRail did receive it");

    smartRail.dropAfterReading = false;
    assert.deepEqual(await pass(at(2)), { attempted: 1, delivered: 1 });
    assert.equal(smartRail.received.length, 2);
    assert.equal(smartRail.received[0]!.body, smartRail.received[1]!.body);
    assert.equal(smartRail.received[0]!.headers["x-railbites-event-id"], smartRail.received[1]!.headers["x-railbites-event-id"]);
  });

  test("retrying callbacks never changes an order or its stock", async () => {
    const link = newLink();
    const order = (await place(app, link)).body;
    const stock = stockOf(app, "pdn-lamprais");
    smartRail.status = 503;
    for (let i = 0; i < 4; i++) await pass(at(i * 10));

    assert.equal(stockOf(app, "pdn-lamprais"), stock);
    const after = (await call(app, "GET", `/v1/order-links/${link}`, { headers: { "X-RailBites-Service-Key": "test-service-key" } })).body;
    assert.deepEqual([after.status, after.statusSequence], [order.status, order.statusSequence]);
    assert.equal(rows().length, 1);
  });

  test("callbacks go out oldest first, one per status change", async () => {
    const link = newLink();
    const order = (await place(app, link)).body;
    await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/ready`);
    await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/collected`, { pickupCode: order.pickupCode });

    await pass(NOW);

    const sequences = smartRail.received.map((r) => (JSON.parse(r.body) as { statusSequence: number }).statusSequence);
    assert.deepEqual(sequences, [1, 2, 3]);
    assert.deepEqual(smartRail.received.map((r) => (JSON.parse(r.body) as { status: string }).status), ["ACCEPTED", "READY", "COLLECTED"]);
  });

  test("with no secret or no SmartRail address nothing is sent and nothing is lost", async () => {
    await place(app, newLink());
    assert.deepEqual(await deliverDueCallbacks(app.db, { ...app.config, callbackSecret: undefined }, NOW), { attempted: 0, delivered: 0 });
    assert.deepEqual(await deliverDueCallbacks(app.db, { ...app.config, smartRailBaseUrl: undefined }, NOW), { attempted: 0, delivered: 0 });
    assert.equal(smartRail.received.length, 0);
    assert.equal(rows()[0]!.delivered_at, null);
  });

  test("pending callbacks survive a restart and are sent afterwards", async () => {
    await place(app, newLink());
    await placePolgahawela(app, newLink());
    smartRail.status = 503;
    await pass(NOW);
    await app.close();

    smartRail.status = 200;
    app = await startApp({
      databasePath: database.path,
      overrides: { smartRailBaseUrl: url, callbackSecret: SECRET },
    });
    assert.deepEqual(await pass(at(5)), { attempted: 2, delivered: 2 });
    assert.ok(rows().every((row) => row.delivered_at !== null));
  });
});
