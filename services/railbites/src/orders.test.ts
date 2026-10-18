import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, test } from "node:test";
import {
  cancel,
  count,
  ETA,
  EVIDENCE,
  JOURNEY_ID,
  kiosk,
  lookup,
  newLink,
  PERADENIYA_STOP,
  peradeniyaPickup,
  place,
  placement,
  POLGAHAWELA_STOP,
  polgahawelaPickup,
  snapshot,
  stockOf,
} from "./order-fixtures";
import { startApp, temporaryDatabasePath, type TestApp } from "./test-support";

let database: ReturnType<typeof temporaryDatabasePath>;
let app: TestApp;

beforeEach(async () => {
  database = temporaryDatabasePath();
  app = await startApp({ databasePath: database.path });
});

afterEach(async () => {
  await app.close();
  database.remove();
});

const outbox = () =>
  app.db.prepare("SELECT event_id, status_sequence, payload FROM webhook_outbox ORDER BY created_at, status_sequence").all() as {
    event_id: string;
    status_sequence: number;
    payload: string;
  }[];

describe("placing an order", () => {
  test("creates an ACCEPTED order with a pickup code, snapshots prices, deducts stock and queues the first callback", async () => {
    const link = newLink();
    const res = await place(app, link, placement({ items: [{ menuItemId: "pdn-lamprais", quantity: 2 }, { menuItemId: "pdn-fish-bun", quantity: 1 }] }));

    assert.equal(res.status, 201);
    assert.equal(res.body.status, "ACCEPTED");
    assert.equal(res.body.statusSequence, 1);
    assert.equal(res.body.orderLinkId, link);
    assert.match(res.body.pickupCode, /^\d{6}$/);
    assert.equal(res.body.totalMinorLkr, 2 * 85000 + 10000);
    assert.deepEqual(res.body.pickupTime, { scheduledArrival: "2026-10-18T07:10:00.000Z", estimatedArrival: ETA, etaEvidenceAt: EVIDENCE });
    assert.equal(stockOf(app, "pdn-lamprais"), 23);
    assert.equal(stockOf(app, "pdn-fish-bun"), 59);

    const queued = outbox();
    assert.equal(queued.length, 1);
    const payload = JSON.parse(queued[0]!.payload);
    assert.equal(payload.orderLinkId, link);
    assert.equal(payload.providerOrderRef, res.body.id);
    assert.equal(payload.status, "ACCEPTED");
    assert.equal(payload.statusSequence, 1);
    assert.ok(!queued[0]!.payload.includes(res.body.pickupCode), "callbacks never carry the pickup code");
  });

  test("later menu price changes do not alter an existing order", async () => {
    const link = newLink();
    await place(app, link);
    app.db.prepare("UPDATE menu_items SET price_minor_lkr = 1 WHERE id = 'pdn-lamprais'").run();
    assert.equal((await lookup(app, link)).body.items[0].unitPriceMinorLkr, 85000);
  });

  test("ten concurrent identical retries create one order and deduct stock once", async () => {
    const link = newLink();
    const results = await Promise.all(Array.from({ length: 10 }, () => place(app, link)));

    assert.equal(results.filter((r) => r.status === 201).length, 1);
    assert.equal(results.filter((r) => r.status === 200).length, 9);
    assert.equal(new Set(results.map((r) => r.body.id)).size, 1);
    assert.equal(new Set(results.map((r) => r.body.pickupCode)).size, 1);
    assert.equal(count(app, "orders"), 1);
    assert.equal(count(app, "webhook_outbox"), 1);
    assert.equal(stockOf(app, "pdn-lamprais"), 24);
  });

  test("a retry may carry a fresher ETA or journey version and still returns the same order", async () => {
    const link = newLink();
    const first = await place(app, link);
    const retry = await place(app, link, placement({ journey: snapshot({ version: 2 }), pickup: peradeniyaPickup({ estimatedArrival: "2026-10-18T07:40:00.000Z" }) }));
    assert.equal(retry.status, 200);
    assert.equal(retry.body.id, first.body.id);
    assert.equal(stockOf(app, "pdn-lamprais"), 24);
  });

  test("reusing a link ID for different items, journey, pickup or collection point is a 409", async () => {
    const link = newLink();
    await place(app, link);
    const different = [
      placement({ items: [{ menuItemId: "pdn-lamprais", quantity: 2 }] }),
      placement({ items: [{ menuItemId: "pdn-fish-bun", quantity: 1 }] }),
      placement({ journey: snapshot({ journeyId: "11111111-1111-4111-8111-111111111111" }) }),
      placement({ pickup: peradeniyaPickup({ stopId: "22222222-2222-4222-8222-222222222222" }) }),
      placement({ collectionPointId: "polgahawela-junction", pickup: polgahawelaPickup(), items: [{ menuItemId: "plg-plain-tea", quantity: 1 }] }),
    ];
    for (const body of different) {
      const res = await place(app, link, body);
      assert.equal(res.status, 409);
      assert.equal(res.body.code, "IDEMPOTENCY_CONFLICT");
    }
    assert.equal(count(app, "orders"), 1);
    assert.equal(stockOf(app, "pdn-lamprais"), 24);
  });

  test("the same items under different link IDs make separate orders with separate codes", async () => {
    const a = await place(app, newLink());
    const b = await place(app, newLink());
    assert.notEqual(a.body.id, b.body.id);
    assert.notEqual(a.body.pickupCode, b.body.pickupCode);
    assert.equal(stockOf(app, "pdn-lamprais"), 23);
  });

  test("a repeated link ID returns the order's current status without new-order checks", async () => {
    const link = newLink();
    const first = await place(app, link);
    app.db.prepare("UPDATE menu_items SET stock_quantity = 0 WHERE id = 'pdn-lamprais'").run();
    app.setNow("2026-10-18T07:00:00.000Z"); // past the cutoff
    const repeat = await place(app, link);
    assert.equal(repeat.status, 200);
    assert.equal(repeat.body.id, first.body.id);
  });
});

describe("refusing an order", () => {
  const untouched = (link: string) => {
    assert.equal(count(app, "orders"), 0);
    assert.equal(count(app, "webhook_outbox"), 0);
    assert.equal(stockOf(app, "pdn-lamprais"), 25);
    assert.equal(count(app, "order_items"), 0);
    return lookup(app, link).then((r) => assert.equal(r.status, 404));
  };

  test("not enough stock is STOCK_INSUFFICIENT and reserves nothing, even for the other items in the request", async () => {
    const link = newLink();
    const res = await place(app, link, placement({ items: [{ menuItemId: "pdn-fish-bun", quantity: 1 }, { menuItemId: "pdn-lamprais", quantity: 20 }, { menuItemId: "pdn-lamprais-x", quantity: 1 }] }));
    assert.equal(res.status, 422);
    assert.equal(res.body.code, "ITEM_UNAVAILABLE");

    const over = await place(app, link, placement({ items: [{ menuItemId: "pdn-fish-bun", quantity: 1 }, { menuItemId: "pdn-lamprais", quantity: 20 }] }));
    assert.equal(over.status, 201);
    const second = await place(app, newLink(), placement({ items: [{ menuItemId: "pdn-fish-bun", quantity: 1 }, { menuItemId: "pdn-lamprais", quantity: 6 }] }));
    assert.equal(second.status, 422);
    assert.equal(second.body.code, "STOCK_INSUFFICIENT");
    assert.deepEqual(second.body.details, { menuItemId: "pdn-lamprais", available: 5 });
    assert.equal(stockOf(app, "pdn-fish-bun"), 59, "the fish bun in the failed request was not reserved");
  });

  test("an unknown, inactive or other-point item is ITEM_UNAVAILABLE", async () => {
    app.db.prepare("UPDATE menu_items SET active = 0 WHERE id = 'pdn-fish-bun'").run();
    for (const menuItemId of ["pdn-fish-bun", "plg-plain-tea", "nonsense"]) {
      const res = await place(app, newLink(), placement({ items: [{ menuItemId, quantity: 1 }] }));
      assert.equal(res.status, 422);
      assert.equal(res.body.code, "ITEM_UNAVAILABLE");
    }
    assert.equal(count(app, "orders"), 0);
  });

  test("a station that is not the collection point's is PICKUP_MISMATCH; an unknown collection point is 404", async () => {
    const mismatch = await place(app, newLink(), placement({ pickup: polgahawelaPickup() }));
    assert.equal(mismatch.status, 422);
    assert.equal(mismatch.body.code, "PICKUP_MISMATCH");
    const unknown = await place(app, newLink(), placement({ collectionPointId: "nowhere" }));
    assert.equal(unknown.status, 404);
    await untouched(newLink());
  });

  test("an unknown ETA, or evidence of 240 seconds or older, is TRAIN_UNKNOWN; 239 seconds is usable", async () => {
    const noEta = await place(app, newLink(), placement({ pickup: peradeniyaPickup({ estimatedArrival: null, etaEvidenceAt: null }) }));
    assert.equal(noEta.status, 422);
    assert.equal(noEta.body.code, "TRAIN_UNKNOWN");

    const stale = await place(app, newLink(), placement({ pickup: peradeniyaPickup({ etaEvidenceAt: "2026-10-18T04:56:00.000Z" }) }));
    assert.equal(stale.body.code, "TRAIN_UNKNOWN");

    const usable = await place(app, newLink(), placement({ pickup: peradeniyaPickup({ etaEvidenceAt: "2026-10-18T04:56:01.000Z" }) }));
    assert.equal(usable.status, 201);
  });

  test("an unknown train does not cancel an existing order", async () => {
    const link = newLink();
    await place(app, link);
    await place(app, newLink(), placement({ pickup: peradeniyaPickup({ estimatedArrival: null, etaEvidenceAt: null }) }));
    assert.equal((await lookup(app, link)).body.status, "ACCEPTED");
  });

  test("the default 30 minute cutoff: one millisecond before is open, equality is closed", async () => {
    app.setNow("2026-10-18T06:54:59.999Z");
    const open = await place(app, newLink(), placement({ pickup: peradeniyaPickup({ etaEvidenceAt: "2026-10-18T06:54:00.000Z" }) }));
    assert.equal(open.status, 201);

    app.setNow("2026-10-18T06:55:00.000Z");
    const closed = await place(app, newLink(), placement({ pickup: peradeniyaPickup({ etaEvidenceAt: "2026-10-18T06:54:00.000Z" }) }));
    assert.equal(closed.status, 422);
    assert.equal(closed.body.code, "CUTOFF_PASSED");
  });

  test("raising the provider cutoff to 45 minutes takes effect at once with no request change", async () => {
    app.setNow("2026-10-18T06:45:00.000Z"); // 40 minutes before the ETA
    const pickup = peradeniyaPickup({ etaEvidenceAt: "2026-10-18T06:44:00.000Z" });
    assert.equal((await place(app, newLink(), placement({ pickup }))).status, 201);

    app.db.prepare("UPDATE collection_points SET cutoff_minutes = 45 WHERE id = 'peradeniya-junction'").run();
    const closed = await place(app, newLink(), placement({ pickup }));
    assert.equal(closed.status, 422);
    assert.equal(closed.body.code, "CUTOFF_PASSED");
    assert.deepEqual(closed.body.details, { cutoffMinutes: 45 });

    app.setNow("2026-10-18T06:39:59.000Z"); // 45 minutes and 1 second before
    assert.equal((await place(app, newLink(), placement({ pickup: peradeniyaPickup({ etaEvidenceAt: "2026-10-18T06:39:00.000Z" }) }))).status, 201);
  });

  test("malformed requests are 400 and change nothing", async () => {
    const bad: unknown[] = [
      placement({ items: [] }),
      placement({ items: [{ menuItemId: "pdn-lamprais", quantity: 0 }] }),
      placement({ items: [{ menuItemId: "pdn-lamprais", quantity: 1.5 }] }),
      placement({ items: [{ menuItemId: "pdn-lamprais", quantity: 1 }, { menuItemId: "pdn-lamprais", quantity: 1 }] }),
      placement({ journey: snapshot({ journeyId: "not-a-uuid" }) }),
      placement({ pickup: peradeniyaPickup({ estimatedArrival: ETA, etaEvidenceAt: null }) }),
      placement({ pickup: peradeniyaPickup({ etaEvidenceAt: "2026-10-18T05:30:00.000Z" }) }), // evidence from the future
      placement({ pickup: peradeniyaPickup({ scheduledArrival: "tomorrow" }) }),
      { collectionPointId: "peradeniya-junction" },
      [],
    ];
    for (const body of bad) {
      const res = await place(app, newLink(), body);
      assert.equal(res.status, 400, JSON.stringify(body).slice(0, 80));
      assert.equal(res.body.code, "INVALID_REQUEST");
    }
    const garbage = await fetch(`${app.baseUrl}/v1/order-links/${newLink()}`, { method: "PUT", headers: { "X-RailBites-Service-Key": "test-service-key" }, body: "{nope" });
    assert.equal(garbage.status, 400);
    const badLink = await place(app, "not-a-uuid");
    assert.equal(badLink.status, 400);
    assert.equal(count(app, "orders"), 0);
  });

  test("an oversized body is refused", async () => {
    const huge = await fetch(`${app.baseUrl}/v1/order-links/${newLink()}`, { method: "PUT", headers: { "X-RailBites-Service-Key": "test-service-key" }, body: JSON.stringify({ pad: "x".repeat(70_000) }) });
    assert.equal(huge.status, 413);
  });

  test("order routes need the service credential", async () => {
    const link = newLink();
    const res = await fetch(`${app.baseUrl}/v1/order-links/${link}`, { method: "PUT", body: JSON.stringify(placement()) });
    assert.equal(res.status, 401);
    assert.equal((await fetch(`${app.baseUrl}/v1/order-links/${link}`)).status, 401);
    assert.equal(count(app, "orders"), 0);
  });
});

describe("journey eligibility", () => {
  const BLOCKED = snapshot({ version: 5, blockedPickupStopIds: [PERADENIYA_STOP] });

  test("an early ending cancels the open order beyond it once, restores stock once and queues one callback", async () => {
    const link = newLink();
    const order = await place(app, link);
    const other = await place(app, newLink(), placement({ collectionPointId: "polgahawela-junction", pickup: polgahawelaPickup(), items: [{ menuItemId: "plg-plain-tea", quantity: 1 }] }));
    assert.equal(stockOf(app, "pdn-lamprais"), 24);

    // The provider learns of the restriction through a later placement's snapshot.
    const blocked = await place(app, newLink(), placement({ journey: BLOCKED }));
    assert.equal(blocked.status, 422);
    assert.equal(blocked.body.code, "STOP_NOT_SERVED");

    const cancelled = await lookup(app, link);
    assert.equal(cancelled.body.status, "CANCELLED");
    assert.equal(cancelled.body.cancellationReason, "TRAIN_ENDED_EARLY");
    assert.equal(cancelled.body.statusSequence, 2);
    assert.equal(stockOf(app, "pdn-lamprais"), 25);
    assert.equal((await lookup(app, other.body.orderLinkId)).body.status, "ACCEPTED", "an earlier stop is unaffected");

    // Seeing the same restriction again changes nothing.
    await place(app, newLink(), placement({ journey: BLOCKED }));
    assert.equal(stockOf(app, "pdn-lamprais"), 25);
    assert.equal(outbox().filter((o) => JSON.parse(o.payload).orderLinkId === link).length, 2);
    assert.equal(order.body.id, cancelled.body.id);
  });

  test("a train cancellation closes the whole journey and cancels with TRAIN_CANCELLED", async () => {
    const link = newLink();
    await place(app, link);
    const res = await place(app, newLink(), placement({ journey: snapshot({ version: 2, orderClosed: true }) }));
    assert.equal(res.body.code, "STOP_NOT_SERVED");
    const order = (await lookup(app, link)).body;
    assert.equal(order.status, "CANCELLED");
    assert.equal(order.cancellationReason, "TRAIN_CANCELLED");
  });

  test("an older version cannot loosen a restriction the provider already holds", async () => {
    await place(app, newLink(), placement({ journey: BLOCKED }));
    const stale = await place(app, newLink(), placement({ journey: snapshot({ version: 4 }) }));
    assert.equal(stale.status, 422);
    assert.equal(stale.body.code, "STOP_NOT_SERVED");
    assert.equal(count(app, "orders"), 0);
  });

  test("the same version with a different snapshot is a conflict", async () => {
    await place(app, newLink(), placement({ journey: BLOCKED }));
    const res = await place(app, newLink(), placement({ journey: snapshot({ version: 5, blockedPickupStopIds: [] }) }));
    assert.equal(res.status, 409);
    assert.equal(res.body.code, "JOURNEY_STATE_CONFLICT");
  });

  test("a withdrawal reopens new orders but never revives a cancelled one", async () => {
    const link = newLink();
    await place(app, link);
    await place(app, newLink(), placement({ journey: BLOCKED }));
    assert.equal((await lookup(app, link)).body.status, "CANCELLED");

    const reopened = await place(app, newLink(), placement({ journey: snapshot({ version: 6 }) }));
    assert.equal(reopened.status, 201);
    const old = (await lookup(app, link)).body;
    assert.equal(old.status, "CANCELLED");
    assert.equal(stockOf(app, "pdn-lamprais"), 24, "only the new order holds stock");

    // Redelivering the old restriction cannot close the stop again.
    const replay = await place(app, newLink(), placement({ journey: BLOCKED }));
    assert.equal(replay.status, 201);
  });

  test("collected and no-show orders keep their status when the stop is later blocked", async () => {
    const collected = newLink();
    const noShow = newLink();
    const a = (await place(app, collected)).body;
    const b = (await place(app, noShow)).body;
    await kiosk(app, "peradeniya-junction", "POST", `/${a.id}/ready`);
    await kiosk(app, "peradeniya-junction", "POST", `/${a.id}/collected`, { pickupCode: a.pickupCode });
    app.setPassage("PASSED");
    await kiosk(app, "peradeniya-junction", "POST", `/${b.id}/no-show`);

    await place(app, newLink(), placement({ journey: BLOCKED }));
    assert.equal((await lookup(app, collected)).body.status, "COLLECTED");
    assert.equal((await lookup(app, noShow)).body.status, "NO_SHOW");
  });
});

describe("passenger cancellation", () => {
  test("before the cutoff it cancels with a reason, restores stock and queues a callback", async () => {
    const link = newLink();
    await place(app, link);
    const res = await cancel(app, link);
    assert.equal(res.status, 200);
    assert.equal(res.body.status, "CANCELLED");
    assert.equal(res.body.cancellationReason, "PASSENGER_CANCELLED");
    assert.equal(res.body.statusSequence, 2);
    assert.equal(stockOf(app, "pdn-lamprais"), 25);
    const queued = outbox();
    assert.equal(queued.length, 2);
    assert.equal(JSON.parse(queued[1]!.payload).reasonCode, "PASSENGER_CANCELLED");
  });

  test("repeating a cancellation is harmless: no second stock return and no second callback", async () => {
    const link = newLink();
    await place(app, link);
    await cancel(app, link);
    const again = await cancel(app, link);
    assert.equal(again.status, 200);
    assert.equal(again.body.statusSequence, 2);
    assert.equal(stockOf(app, "pdn-lamprais"), 25);
    assert.equal(outbox().length, 2);
  });

  test("at the cutoff exactly, and after it, cancellation is refused", async () => {
    const link = newLink();
    await place(app, link);
    const fresh = peradeniyaPickup({ etaEvidenceAt: "2026-10-18T06:54:00.000Z" });

    app.setNow("2026-10-18T06:54:59.999Z");
    const early = await cancel(app, link, { journey: snapshot(), pickup: fresh });
    assert.equal(early.status, 200, "one millisecond before the cutoff is still open");

    const second = newLink();
    app.setNow("2026-10-18T05:00:00.000Z");
    await place(app, second);
    app.setNow("2026-10-18T06:55:00.000Z");
    const closed = await cancel(app, second, { journey: snapshot(), pickup: fresh });
    assert.equal(closed.status, 409);
    assert.equal(closed.body.code, "CUTOFF_PASSED");
    assert.equal((await lookup(app, second)).body.status, "ACCEPTED");
    assert.equal(stockOf(app, "pdn-lamprais"), 24);
  });

  test("with the cutoff provider-owned, 45 minutes closes a cancellation that 30 would allow", async () => {
    const link = newLink();
    await place(app, link);
    app.setNow("2026-10-18T06:45:00.000Z");
    app.db.prepare("UPDATE collection_points SET cutoff_minutes = 45 WHERE id = 'peradeniya-junction'").run();
    const res = await cancel(app, link, { journey: snapshot(), pickup: peradeniyaPickup({ etaEvidenceAt: "2026-10-18T06:44:00.000Z" }) });
    assert.equal(res.status, 409);
    assert.equal(res.body.code, "CUTOFF_PASSED");
  });

  test("an unknown or stale ETA cannot confirm the cutoff", async () => {
    const link = newLink();
    await place(app, link);
    const unknown = await cancel(app, link, { journey: snapshot(), pickup: peradeniyaPickup({ estimatedArrival: null, etaEvidenceAt: null }) });
    assert.equal(unknown.status, 409);
    assert.equal(unknown.body.code, "TRAIN_UNKNOWN");
    app.setNow("2026-10-18T05:10:00.000Z");
    const stale = await cancel(app, link);
    assert.equal(stale.body.code, "TRAIN_UNKNOWN");
    assert.equal((await lookup(app, link)).body.status, "ACCEPTED");
  });

  test("a snapshot for another journey or stop, or an unknown link, is refused", async () => {
    const link = newLink();
    await place(app, link);
    const wrongStop = await cancel(app, link, { journey: snapshot(), pickup: peradeniyaPickup({ stopId: POLGAHAWELA_STOP }) });
    assert.equal(wrongStop.status, 422);
    const wrongJourney = await cancel(app, link, { journey: snapshot({ journeyId: "11111111-1111-4111-8111-111111111111" }), pickup: peradeniyaPickup() });
    assert.equal(wrongJourney.status, 422);
    assert.equal((await cancel(app, newLink())).status, 404);
    assert.equal((await cancel(app, link, { journey: snapshot() })).status, 400);
  });

  test("a collected or no-show order cannot be cancelled", async () => {
    const link = newLink();
    const order = (await place(app, link)).body;
    await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/ready`);
    await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/collected`, { pickupCode: order.pickupCode });
    const res = await cancel(app, link);
    assert.equal(res.status, 409);
    assert.equal(res.body.code, "INVALID_STATE");
    assert.equal(stockOf(app, "pdn-lamprais"), 24);
  });

  test("a newer restriction in the cancellation snapshot cancels for the train, not the passenger", async () => {
    const link = newLink();
    await place(app, link);
    const res = await cancel(app, link, { journey: snapshot({ version: 3, orderClosed: true }), pickup: peradeniyaPickup() });
    assert.equal(res.status, 200);
    assert.equal(res.body.cancellationReason, "TRAIN_CANCELLED");
  });
});

describe("kiosk credentials", () => {
  test("a missing, unknown or service credential is 401; another point's kiosk key is 403", async () => {
    const path = "/v1/kiosk/collection-points/peradeniya-junction/orders";
    const bare = await fetch(`${app.baseUrl}${path}`);
    assert.equal(bare.status, 401);
    const wrong = await fetch(`${app.baseUrl}${path}`, { headers: { "X-RailBites-Kiosk-Key": "nope" } });
    assert.equal(wrong.status, 401);
    const service = await fetch(`${app.baseUrl}${path}`, { headers: { "X-RailBites-Kiosk-Key": "test-service-key", "X-RailBites-Service-Key": "test-service-key" } });
    assert.equal(service.status, 401);
    const other = await fetch(`${app.baseUrl}${path}`, { headers: { "X-RailBites-Kiosk-Key": "kiosk-key-polgahawela" } });
    assert.equal(other.status, 403);
    assert.equal(((await other.json()) as { code: string }).code, "FORBIDDEN_COLLECTION_POINT");
  });

  test("a kiosk key does not open service routes", async () => {
    const res = await fetch(`${app.baseUrl}/v1/collection-points`, { headers: { "X-RailBites-Service-Key": "kiosk-key-peradeniya" } });
    assert.equal(res.status, 401);
  });

  test("a kiosk cannot act on, or even see, another point's order", async () => {
    const order = (await place(app, newLink())).body;
    const list = await kiosk(app, "polgahawela-junction", "GET", "");
    assert.deepEqual(list.body.orders, []);
    for (const action of ["ready", "collected", "no-show"]) {
      const res = await kiosk(app, "polgahawela-junction", "POST", `/${order.id}/${action}`, { pickupCode: order.pickupCode });
      assert.equal(res.status, 404, action);
    }
    const crossKey = await fetch(`${app.baseUrl}/v1/kiosk/collection-points/peradeniya-junction/orders/${order.id}/ready`, {
      method: "POST",
      headers: { "X-RailBites-Kiosk-Key": "kiosk-key-polgahawela" },
    });
    assert.equal(crossKey.status, 403);
    assert.equal((await lookup(app, order.orderLinkId)).body.status, "ACCEPTED");
  });
});

describe("kiosk actions", () => {
  test("lists only its own point's orders, soonest pickup first, without pickup codes or passenger data", async () => {
    const first = (await place(app, newLink())).body;
    await place(app, newLink(), placement({ pickup: peradeniyaPickup({ estimatedArrival: "2026-10-18T07:20:00.000Z" }) }));
    await place(app, newLink(), placement({ collectionPointId: "polgahawela-junction", pickup: polgahawelaPickup(), items: [{ menuItemId: "plg-plain-tea", quantity: 1 }] }));

    const list = await kiosk(app, "peradeniya-junction", "GET", "");
    assert.equal(list.status, 200);
    assert.equal(list.body.orders.length, 2);
    assert.equal(list.body.orders[0].pickupTime.estimatedArrival, "2026-10-18T07:20:00.000Z");
    assert.ok(list.body.orders.some((o: { id: string }) => o.id === first.id));
    const text = JSON.stringify(list.body);
    assert.ok(!text.includes(first.pickupCode) && !text.includes("pickupCode") && !text.includes("orderLinkId"));

    assert.equal((await kiosk(app, "peradeniya-junction", "GET", "?status=READY")).body.orders.length, 0);
    assert.equal((await kiosk(app, "peradeniya-junction", "GET", "?status=BOGUS")).status, 400);
  });

  test("ACCEPTED -> READY -> COLLECTED with the matching code, one callback per change", async () => {
    const order = (await place(app, newLink())).body;
    const ready = await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/ready`);
    assert.equal(ready.status, 200);
    assert.equal(ready.body.status, "READY");
    assert.equal(ready.body.statusSequence, 2);

    const wrong = await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/collected`, { pickupCode: "000000" === order.pickupCode ? "111111" : "000000" });
    assert.equal(wrong.status, 422);
    assert.equal(wrong.body.code, "PICKUP_CODE_MISMATCH");
    assert.equal((await lookup(app, order.orderLinkId)).body.status, "READY");

    const collected = await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/collected`, { pickupCode: order.pickupCode });
    assert.equal(collected.status, 200);
    assert.equal(collected.body.status, "COLLECTED");
    assert.equal(collected.body.statusSequence, 3);
    assert.deepEqual(outbox().map((o) => JSON.parse(o.payload).status), ["ACCEPTED", "READY", "COLLECTED"]);
    assert.equal(stockOf(app, "pdn-lamprais"), 24, "collection never returns stock");
  });

  test("repeating a ready or collected action is harmless and adds no callback", async () => {
    const order = (await place(app, newLink())).body;
    await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/ready`);
    assert.equal((await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/ready`)).body.statusSequence, 2);
    await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/collected`, { pickupCode: order.pickupCode });
    const again = await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/collected`, { pickupCode: order.pickupCode });
    assert.equal(again.status, 200);
    assert.equal(again.body.statusSequence, 3);
    assert.equal(outbox().length, 3);
  });

  test("collection is refused before READY, and conflicting terminal actions are 409", async () => {
    const order = (await place(app, newLink())).body;
    const early = await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/collected`, { pickupCode: order.pickupCode });
    assert.equal(early.status, 409);
    assert.equal(early.body.code, "INVALID_STATE");
    assert.equal((await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/collected`, {})).status, 400);

    await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/ready`);
    await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/collected`, { pickupCode: order.pickupCode });
    app.setPassage("PASSED");
    assert.equal((await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/no-show`)).status, 409);
    assert.equal((await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/ready`)).status, 409);
  });

  test("a cancelled order cannot be readied, collected or marked no-show", async () => {
    const link = newLink();
    const order = (await place(app, link)).body;
    await cancel(app, link);
    app.setPassage("PASSED");
    for (const action of ["ready", "collected", "no-show"]) {
      const res = await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/${action}`, { pickupCode: order.pickupCode });
      assert.equal(res.status, 409, action);
    }
  });

  test("no-show needs SmartRail to confirm the train passed; scheduled time and kiosk claims are not evidence", async () => {
    const order = (await place(app, newLink())).body;
    app.setNow("2026-10-18T09:00:00.000Z"); // long after the scheduled arrival

    for (const state of ["UNKNOWN", "AHEAD"] as const) {
      app.setPassage(state);
      const res = await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/no-show`, { passed: true });
      assert.equal(res.status, 409);
      assert.equal(res.body.code, "TRAIN_NOT_PASSED");
    }
    app.setPassage("ERROR");
    const down = await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/no-show`);
    assert.equal(down.status, 503);
    assert.equal(down.body.code, "SMARTRAIL_UNAVAILABLE");
    assert.equal((await lookup(app, order.orderLinkId)).body.status, "ACCEPTED");

    app.setPassage("PASSED");
    const done = await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/no-show`);
    assert.equal(done.status, 200);
    assert.equal(done.body.status, "NO_SHOW");
    assert.equal(done.body.statusSequence, 2);
    assert.equal(stockOf(app, "pdn-lamprais"), 24, "a no-show does not return stock");

    const again = await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/no-show`);
    assert.equal(again.status, 200);
    assert.equal(again.body.statusSequence, 2);
    assert.equal(outbox().length, 2);
    assert.equal((await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/collected`, { pickupCode: order.pickupCode })).status, 409);
  });
});

describe("durability", () => {
  test("orders, codes, stock and the pending callbacks survive a restart, and a repeated PUT still returns the same order", async () => {
    const link = newLink();
    const order = (await place(app, link)).body;
    await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/ready`);
    await app.close();

    app = await startApp({ databasePath: database.path });
    const again = await place(app, link);
    assert.equal(again.status, 200);
    assert.equal(again.body.id, order.id);
    assert.equal(again.body.pickupCode, order.pickupCode);
    assert.equal(again.body.status, "READY");
    assert.equal(stockOf(app, "pdn-lamprais"), 24, "reseeding on restart did not reset stock");
    assert.equal(outbox().length, 2);
    assert.equal((app.db.prepare("SELECT count(*) n FROM schema_migrations").get() as { n: number }).n, 4);
    assert.equal(JOURNEY_ID, order.journeyId);
    assert.equal(PERADENIYA_STOP, order.pickupStopId);
  });
});
