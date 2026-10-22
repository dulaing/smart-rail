import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, test } from "node:test";
import {
  call,
  count,
  deliver,
  ETA,
  EVIDENCE,
  journeyEvent,
  kiosk,
  lookup,
  newLink,
  PERADENIYA_STOP,
  peradeniyaPickup,
  place,
  placePolgahawela,
  placement,
  POLGAHAWELA_STOP,
  polgahawelaPickup,
  snapshot,
  stockOf,
} from "./order-fixtures";
import { serviceHeaders, startApp, temporaryDatabasePath, type TestApp } from "./test-support";

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
  app.db.prepare("SELECT status_sequence, payload FROM webhook_outbox ORDER BY created_at, rowid").all() as unknown as {
    status_sequence: number;
    payload: string;
  }[];

const LATER_ETA = "2026-10-18T07:45:00.000Z";
const NEW_EVIDENCE = "2026-10-18T05:00:00.000Z";

const unknownPeradeniya = () => peradeniyaPickup({ estimatedArrival: null, etaEvidenceAt: null });

const earlyEndBeyondPolgahawela = () =>
  journeyEvent({
    eventType: "EARLY_END",
    journey: snapshot({ version: 2, blockedPickupStopIds: [PERADENIYA_STOP] }),
    stops: [unknownPeradeniya()],
  });

describe("a delay", () => {
  test("moves the pickup time of every open order on the journey, with a callback for each", async () => {
    const pdn = newLink();
    const plg = newLink();
    await place(app, pdn);
    await placePolgahawela(app, plg);

    const res = await deliver(
      app,
      journeyEvent({
        stops: [
          polgahawelaPickup({ estimatedArrival: "2026-10-18T07:10:00.000Z", etaEvidenceAt: NEW_EVIDENCE }),
          peradeniyaPickup({ estimatedArrival: LATER_ETA, etaEvidenceAt: NEW_EVIDENCE }),
        ],
      }),
    );

    assert.equal(res.status, 200);
    assert.equal(res.body.result, "APPLIED");
    const order = (await lookup(app, pdn)).body;
    assert.deepEqual(order.pickupTime, { scheduledArrival: "2026-10-18T07:10:00.000Z", estimatedArrival: LATER_ETA, etaEvidenceAt: NEW_EVIDENCE });
    assert.equal(order.statusSequence, 2);
    assert.equal(order.status, "ACCEPTED");
    assert.equal((await lookup(app, plg)).body.pickupTime.estimatedArrival, "2026-10-18T07:10:00.000Z");

    const sent = outbox().map((o) => JSON.parse(o.payload));
    assert.equal(sent.filter((p) => p.statusSequence === 2).length, 2);
    assert.equal(sent.find((p) => p.orderLinkId === pdn && p.statusSequence === 2).pickupTime.estimatedArrival, LATER_ETA);
  });

  test("the extended pickup time moves the order's cutoff with it", async () => {
    const link = newLink();
    await place(app, link);
    // At 06:56 a new order is too late for the original 07:25 ETA (cutoff 06:55) ...
    app.setNow("2026-10-18T06:56:00.000Z");
    const evidence = "2026-10-18T06:55:00.000Z";
    const tooLate = placement({ pickup: peradeniyaPickup({ etaEvidenceAt: evidence }) });
    assert.equal((await place(app, newLink(), tooLate)).body.code, "CUTOFF_PASSED");

    // ... but with the confirmed delay the same moment is open.
    const delayed = peradeniyaPickup({ estimatedArrival: LATER_ETA, etaEvidenceAt: evidence });
    await deliver(app, journeyEvent({ stops: [delayed] }));
    assert.equal((await place(app, newLink(), placement({ journey: snapshot({ version: 2 }), pickup: delayed }))).status, 201);
  });

  test("an unknown ETA replaces a known one; the schedule is never substituted", async () => {
    const link = newLink();
    await place(app, link);
    await deliver(app, journeyEvent({ stops: [unknownPeradeniya()] }));

    const order = (await lookup(app, link)).body;
    assert.deepEqual(order.pickupTime, { scheduledArrival: "2026-10-18T07:10:00.000Z", estimatedArrival: null, etaEvidenceAt: null });
    assert.equal(order.status, "ACCEPTED");
    assert.equal(order.statusSequence, 2);
  });

  test("an event that changes nothing visible adds no sequence and no callback", async () => {
    const link = newLink();
    await place(app, link);
    await deliver(app, journeyEvent({ stops: [peradeniyaPickup({ estimatedArrival: ETA, etaEvidenceAt: EVIDENCE })] }));
    assert.equal((await lookup(app, link)).body.statusSequence, 1);
    assert.equal(outbox().length, 1);
  });

  test("a terminal order keeps its history when a later delay arrives", async () => {
    const link = newLink();
    const order = (await place(app, link)).body;
    await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/ready`);
    await kiosk(app, "peradeniya-junction", "POST", `/${order.id}/collected`, { pickupCode: order.pickupCode });

    await deliver(app, journeyEvent({ stops: [peradeniyaPickup({ estimatedArrival: LATER_ETA, etaEvidenceAt: NEW_EVIDENCE })] }));

    const after = (await lookup(app, link)).body;
    assert.equal(after.status, "COLLECTED");
    assert.equal(after.pickupTime.estimatedArrival, ETA);
    assert.equal(after.statusSequence, 3);
  });
});

describe("a cancellation or early ending", () => {
  test("a cancelled journey cancels every open order with a reason and returns stock once", async () => {
    const pdn = newLink();
    const plg = newLink();
    await place(app, pdn);
    await placePolgahawela(app, plg);

    const res = await deliver(
      app,
      journeyEvent({
        eventType: "CANCEL",
        journey: snapshot({ version: 2, orderClosed: true, blockedPickupStopIds: [POLGAHAWELA_STOP, PERADENIYA_STOP] }),
        stops: [polgahawelaPickup({ estimatedArrival: null, etaEvidenceAt: null }), unknownPeradeniya()],
      }),
    );

    assert.equal(res.body.result, "APPLIED");
    for (const link of [pdn, plg]) {
      const order = (await lookup(app, link)).body;
      assert.equal(order.status, "CANCELLED");
      assert.equal(order.cancellationReason, "TRAIN_CANCELLED");
      assert.equal(order.statusSequence, 2);
    }
    assert.equal(stockOf(app, "pdn-lamprais"), 25);
    assert.equal(stockOf(app, "plg-plain-tea"), 80);
    assert.equal(outbox().length, 4);
  });

  test("an early ending keeps the new final stop, with its updated time, and cancels only the stops beyond it", async () => {
    const final = newLink();
    const beyond = newLink();
    await placePolgahawela(app, final);
    await place(app, beyond);

    await deliver(
      app,
      journeyEvent({
        eventType: "EARLY_END",
        journey: snapshot({ version: 2, blockedPickupStopIds: [PERADENIYA_STOP] }),
        stops: [polgahawelaPickup({ estimatedArrival: "2026-10-18T07:00:00.000Z", etaEvidenceAt: NEW_EVIDENCE }), unknownPeradeniya()],
      }),
    );

    const kept = (await lookup(app, final)).body;
    assert.equal(kept.status, "ACCEPTED", "the final stop is still served");
    assert.equal(kept.pickupTime.estimatedArrival, "2026-10-18T07:00:00.000Z");
    const cancelled = (await lookup(app, beyond)).body;
    assert.equal(cancelled.status, "CANCELLED");
    assert.equal(cancelled.cancellationReason, "TRAIN_ENDED_EARLY");
    assert.equal(stockOf(app, "pdn-lamprais"), 25);
    assert.equal(stockOf(app, "plg-plain-tea"), 79);
  });

  test("collected and no-show orders are not changed by a later cancellation", async () => {
    const collected = (await place(app, newLink())).body;
    const noShow = (await place(app, newLink())).body;
    await kiosk(app, "peradeniya-junction", "POST", `/${collected.id}/ready`);
    await kiosk(app, "peradeniya-junction", "POST", `/${collected.id}/collected`, { pickupCode: collected.pickupCode });
    app.setPassage("PASSED");
    await kiosk(app, "peradeniya-junction", "POST", `/${noShow.id}/no-show`);

    await deliver(
      app,
      journeyEvent({ eventType: "CANCEL", journey: snapshot({ version: 2, orderClosed: true }), stops: [unknownPeradeniya()] }),
    );

    assert.equal((await lookup(app, collected.orderLinkId)).body.status, "COLLECTED");
    assert.equal((await lookup(app, noShow.orderLinkId)).body.status, "NO_SHOW");
    assert.equal(stockOf(app, "pdn-lamprais"), 23, "no stock came back for completed orders");
  });

  test("a withdrawal reopens new orders and never revives a cancelled one", async () => {
    const link = newLink();
    await place(app, link);
    const earlyEnd = earlyEndBeyondPolgahawela();
    await deliver(app, earlyEnd);
    assert.equal((await lookup(app, link)).body.status, "CANCELLED");

    const blocked = await place(app, newLink(), placement({ journey: snapshot({ version: 2, blockedPickupStopIds: [PERADENIYA_STOP] }) }));
    assert.equal(blocked.body.code, "STOP_NOT_SERVED");

    await deliver(app, journeyEvent({ eventType: "WITHDRAWAL", journey: snapshot({ version: 3 }), stops: [peradeniyaPickup()] }));

    assert.equal((await lookup(app, link)).body.status, "CANCELLED");
    assert.equal((await lookup(app, link)).body.statusSequence, 2);
    assert.equal((await place(app, newLink(), placement({ journey: snapshot({ version: 3 }) }))).status, 201);

    // Redelivering the old restriction cannot close the stop again.
    const replay = await deliver(app, earlyEnd);
    assert.equal(replay.body.result, "DUPLICATE");
    const olderCopy = await deliver(app, { ...earlyEndBeyondPolgahawela(), eventId: "train-change:another-id" });
    assert.equal(olderCopy.body.result, "STALE");
    assert.equal((await place(app, newLink(), placement({ journey: snapshot({ version: 3 }) }))).status, 201);
  });
});

describe("repeats, order and conflicts", () => {
  test("the same event twice is a harmless duplicate that changes nothing more", async () => {
    const link = newLink();
    await place(app, link);
    const event = earlyEndBeyondPolgahawela();

    assert.equal((await deliver(app, event)).body.result, "APPLIED");
    const callbacks = outbox().length;
    const stock = stockOf(app, "pdn-lamprais");

    const again = await deliver(app, event);
    assert.equal(again.status, 200);
    assert.equal(again.body.result, "DUPLICATE");
    assert.equal(outbox().length, callbacks);
    assert.equal(stockOf(app, "pdn-lamprais"), stock);
    assert.equal((await lookup(app, link)).body.statusSequence, 2);
    assert.equal(count(app, "received_events"), 1);
  });

  test("the same event id with different content is a conflict and changes nothing", async () => {
    const event = journeyEvent();
    await deliver(app, event);
    const changed = { ...event, stops: [peradeniyaPickup({ estimatedArrival: LATER_ETA, etaEvidenceAt: NEW_EVIDENCE })] };
    const res = await deliver(app, changed);
    assert.equal(res.status, 409);
    assert.equal(res.body.code, "EVENT_CONFLICT");
  });

  test("an older version is recorded and ignored, so it cannot undo newer state", async () => {
    const link = newLink();
    await place(app, link);
    await deliver(
      app,
      journeyEvent({ eventType: "EARLY_END", journey: snapshot({ version: 5, blockedPickupStopIds: [PERADENIYA_STOP] }), stops: [unknownPeradeniya()] }),
    );

    const old = await deliver(
      app,
      journeyEvent({ journey: snapshot({ version: 3 }), stops: [peradeniyaPickup({ estimatedArrival: LATER_ETA, etaEvidenceAt: NEW_EVIDENCE })] }),
    );

    assert.equal(old.body.result, "STALE");
    assert.equal(count(app, "received_events"), 2);
    const order = (await lookup(app, link)).body;
    assert.equal(order.status, "CANCELLED");
    assert.equal(order.pickupTime.estimatedArrival, ETA, "a cancelled order keeps the time it had; the older event did not touch it");
  });

  test("versions need not be consecutive", async () => {
    const link = newLink();
    await place(app, link);
    const res = await deliver(
      app,
      journeyEvent({ journey: snapshot({ version: 40 }), stops: [peradeniyaPickup({ estimatedArrival: LATER_ETA, etaEvidenceAt: NEW_EVIDENCE })] }),
    );
    assert.equal(res.body.result, "APPLIED");
    assert.equal((await lookup(app, link)).body.pickupTime.estimatedArrival, LATER_ETA);
  });

  test("the same version with a different eligibility is a conflict", async () => {
    await deliver(app, journeyEvent({ journey: snapshot({ version: 4, blockedPickupStopIds: [PERADENIYA_STOP] }) }));
    const res = await deliver(app, journeyEvent({ journey: snapshot({ version: 4 }) }));
    assert.equal(res.status, 409);
    assert.equal(res.body.code, "JOURNEY_STATE_CONFLICT");
    assert.equal(count(app, "received_events"), 1, "the conflicting event left no receipt");
  });

  test("an event for a journey with no orders still records the state, so a later order is refused", async () => {
    await deliver(app, journeyEvent({ eventType: "CANCEL", journey: snapshot({ version: 2, orderClosed: true }), stops: [] }));
    const res = await place(app, newLink(), placement({ journey: snapshot({ version: 1 }) }));
    assert.equal(res.status, 422);
    assert.equal(res.body.code, "STOP_NOT_SERVED");
  });

  test("a lost-response race: the order placed before the early ending is cancelled, and a lookup says so", async () => {
    const link = newLink();
    await place(app, link); // the placement response is imagined lost
    await deliver(app, earlyEndBeyondPolgahawela());

    const looked = await lookup(app, link);
    assert.equal(looked.body.status, "CANCELLED");
    assert.equal(looked.body.cancellationReason, "TRAIN_ENDED_EARLY");
    const repeat = await place(app, link);
    assert.equal(repeat.status, 200);
    assert.equal(repeat.body.status, "CANCELLED");
  });

  test("a lost-request race: after the early ending, the late original placement is refused", async () => {
    await deliver(app, earlyEndBeyondPolgahawela());

    const late = await place(app, newLink()); // built from the older, version 1 view of the journey
    assert.equal(late.status, 422);
    assert.equal(late.body.code, "STOP_NOT_SERVED");
    assert.equal(count(app, "orders"), 0);
    assert.equal(stockOf(app, "pdn-lamprais"), 25);
  });
});

describe("atomic receipt", () => {
  test("a failure part-way leaves no receipt and no half-applied change, and the redelivery succeeds", async () => {
    const link = newLink();
    await place(app, link);
    // Make queueing the cancellation callback fail, after the order was updated and stock returned.
    app.db.exec(
      "CREATE TRIGGER fail_callbacks BEFORE INSERT ON webhook_outbox WHEN NEW.status_sequence = 2 BEGIN SELECT RAISE(ABORT, 'simulated failure'); END",
    );
    const event = earlyEndBeyondPolgahawela();

    const failed = await deliver(app, event);

    assert.equal(failed.status, 500);
    assert.equal(count(app, "received_events"), 0);
    assert.equal((app.db.prepare("SELECT applied_journey_version v FROM journey_order_states").get() as { v: number }).v, 1);
    assert.equal((await lookup(app, link)).body.status, "ACCEPTED");
    assert.equal(stockOf(app, "pdn-lamprais"), 24);

    app.db.exec("DROP TRIGGER fail_callbacks");
    const redelivered = await deliver(app, event);
    assert.equal(redelivered.body.result, "APPLIED");
    assert.equal((await lookup(app, link)).body.status, "CANCELLED");
    assert.equal(stockOf(app, "pdn-lamprais"), 25);
  });

  test("a received event is still a duplicate after a restart", async () => {
    const link = newLink();
    await place(app, link);
    const event = earlyEndBeyondPolgahawela();
    await deliver(app, event);
    await app.close();

    app = await startApp({ databasePath: database.path });
    assert.equal((await deliver(app, event)).body.result, "DUPLICATE");
    assert.equal(stockOf(app, "pdn-lamprais"), 25);
    assert.equal((await lookup(app, link)).body.status, "CANCELLED");
  });

  test("event delivery needs the service credential and a valid body", async () => {
    const noKey = await call(app, "POST", "/v1/journey-events", { body: journeyEvent() });
    assert.equal(noKey.status, 401);
    const bad = await call(app, "POST", "/v1/journey-events", { headers: serviceHeaders(), body: journeyEvent({ eventType: "SOMETHING" }) });
    assert.equal(bad.status, 400);
    const noTimes = await call(app, "POST", "/v1/journey-events", { headers: serviceHeaders(), body: { ...journeyEvent(), calculatedAt: undefined } });
    assert.equal(noTimes.status, 400);
    assert.equal(count(app, "received_events"), 0);
  });
});
