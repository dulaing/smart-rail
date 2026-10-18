import assert from "node:assert/strict";
import { after, before, test } from "node:test";
import { ETA, PERADENIYA_STOP, cancel, newLink, place, placePolgahawela } from "./order-fixtures";
import { KIOSK_KEYS, SERVICE_KEY, startApp, staffSignIn, temporaryDatabasePath, type TestApp } from "./test-support";

const database = temporaryDatabasePath();
let app: TestApp;

before(async () => {
  app = await startApp({ databasePath: database.path });
});

after(async () => {
  await app.close();
  database.remove();
});

const list = (cookie: string, query = "") =>
  fetch(`${app.baseUrl}/staff/orders${query}`, { headers: { Cookie: cookie }, redirect: "manual" });

test("the order list needs a session", async () => {
  const res = await list("");
  assert.equal(res.status, 401);
  assert.equal(((await res.json()) as { code: string }).code, "NOT_SIGNED_IN");
});

test("staff see their point's orders with train identity but never the pickup code", async () => {
  const order = (await place(app, newLink())).body;
  const { cookie } = await staffSignIn(app, "peradeniya-junction");

  const res = await list(cookie);
  assert.equal(res.status, 200);
  const body = (await res.json()) as { orders: any[] };
  const found = body.orders.find((entry) => entry.id === order.id);
  assert.ok(found, "the order should be listed");
  assert.equal(found.trainNumber, "SIM-3001");
  assert.equal(found.serviceDate, "2026-10-18");
  assert.equal(found.pickupStopId, PERADENIYA_STOP);
  assert.equal(found.pickupTime.estimatedArrival, ETA);
  assert.equal(found.items[0].quantity, 1);

  const text = JSON.stringify(body);
  assert.ok(!text.includes(order.pickupCode));
  assert.ok(!text.includes("pickupCode"));
  assert.ok(!text.includes("orderLinkId"));
});

test("a reasoned cancellation is visible to staff", async () => {
  const link = newLink();
  const order = (await place(app, link)).body;
  await cancel(app, link);
  const { cookie } = await staffSignIn(app, "peradeniya-junction");

  const body = (await (await list(cookie)).json()) as { orders: any[] };
  const found = body.orders.find((entry) => entry.id === order.id);
  assert.equal(found.status, "CANCELLED");
  assert.equal(found.cancellationReason, "PASSENGER_CANCELLED");
});

test("an empty point returns an empty list, not an error", async () => {
  const solo = await startApp({ databasePath: temporaryDatabasePath().path });
  try {
    const { cookie } = await staffSignIn(solo, "peradeniya-junction");
    const res = await fetch(`${solo.baseUrl}/staff/orders`, { headers: { Cookie: cookie } });
    assert.equal(res.status, 200);
    assert.deepEqual(await res.json(), { orders: [] });
  } finally {
    await solo.close();
  }
});

test("a session only ever sees its own collection point's orders", async () => {
  await placePolgahawela(app, newLink());
  const peradeniya = await staffSignIn(app, "peradeniya-junction");
  const polgahawela = await staffSignIn(app, "polgahawela-junction");

  const pdn = (await (await list(peradeniya.cookie)).json()) as { orders: any[] };
  const plg = (await (await list(polgahawela.cookie)).json()) as { orders: any[] };
  assert.ok(pdn.orders.length > 0);
  assert.ok(plg.orders.length > 0);
  assert.ok(pdn.orders.every((entry) => entry.collectionPointId === "peradeniya-junction"));
  assert.ok(plg.orders.every((entry) => entry.collectionPointId === "polgahawela-junction"));
});

test("the orders page shows the point and carries no secret", async () => {
  const { cookie } = await staffSignIn(app, "peradeniya-junction");
  const res = await fetch(`${app.baseUrl}/staff`, { headers: { Cookie: cookie } });
  const html = await res.text();
  assert.match(html, /Peradeniya Junction/);
  assert.match(html, /\/staff\/orders/);
  for (const secret of [SERVICE_KEY, ...KIOSK_KEYS.values()]) {
    assert.ok(!html.includes(secret));
  }
});

test("an expired session stops returning orders", async () => {
  const expiring = await startApp({ databasePath: temporaryDatabasePath().path, now: "2026-10-18T05:00:00.000Z", overrides: { staffSessionLifetimeMs: 1_000 } });
  try {
    await place(expiring, newLink());
    const { cookie } = await staffSignIn(expiring);
    expiring.setNow("2026-10-18T05:00:30.000Z");
    const res = await fetch(`${expiring.baseUrl}/staff/orders`, { headers: { Cookie: cookie } });
    assert.equal(res.status, 401);
  } finally {
    await expiring.close();
  }
});

test("the list can be filtered by status server-side", async () => {
  const ready = (await place(app, newLink())).body;
  const { cookie } = await staffSignIn(app, "peradeniya-junction");
  const res = await fetch(`${app.baseUrl}/v1/kiosk/collection-points/peradeniya-junction/orders/${ready.id}/ready`, {
    method: "POST",
    headers: { "X-RailBites-Kiosk-Key": KIOSK_KEYS.get("peradeniya-junction")! },
  });
  assert.equal(res.status, 200);

  const body = (await (await list(cookie, "?status=READY")).json()) as { orders: any[] };
  assert.ok(body.orders.some((entry) => entry.id === ready.id));
  assert.ok(body.orders.every((entry) => entry.status === "READY"));
});

test("a status filter that is not a real state is rejected", async () => {
  const { cookie } = await staffSignIn(app, "peradeniya-junction");
  const res = await list(cookie, "?status=BOGUS");
  assert.equal(res.status, 400);
  assert.equal(((await res.json()) as { code: string }).code, "INVALID_REQUEST");
});
