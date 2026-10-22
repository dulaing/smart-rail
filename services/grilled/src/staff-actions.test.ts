import assert from "node:assert/strict";
import { after, before, test } from "node:test";
import { newLink, place, placePolgahawela } from "./order-fixtures";
import { startApp, staffSignIn, temporaryDatabasePath, type TestApp } from "./test-support";

const database = temporaryDatabasePath();
let app: TestApp;

before(async () => {
  app = await startApp({ databasePath: database.path });
});

after(async () => {
  await app.close();
  database.remove();
});

// The session CSRF token per cookie, filled by signIn below; every action needs it.
const currentCsrf = new Map<string, string>();

async function signIn(target: TestApp, point = "peradeniya-junction") {
  const session = await staffSignIn(target, point);
  currentCsrf.set(session.cookie, session.csrf);
  return session;
}

function action(
  baseUrl: string,
  cookie: string,
  orderId: string,
  name: string,
  body?: unknown,
  options: { csrf?: string | null; origin?: string } = {},
) {
  const headers: Record<string, string> = { "Content-Type": "application/json", Cookie: cookie };
  if (options.csrf !== null) headers["X-Grilled-CSRF"] = options.csrf ?? currentCsrf.get(cookie)!;
  if (options.origin) headers["Origin"] = options.origin;
  return fetch(`${baseUrl}/staff/orders/${orderId}/${name}`, { method: "POST", headers, body: JSON.stringify(body ?? {}) });
}

const orderStatus = (target: TestApp, id: string) =>
  target.db.prepare("SELECT status, status_sequence FROM orders WHERE id = ?").get(id) as { status: string; status_sequence: number };

test("an action needs a session and a CSRF token", async () => {
  const order = (await place(app, newLink())).body;
  const { cookie } = await signIn(app);

  assert.equal((await fetch(`${app.baseUrl}/staff/orders/${order.id}/ready`, { method: "POST" })).status, 401);
  assert.equal((await action(app.baseUrl, cookie, order.id, "ready", {}, { csrf: null })).status, 403);
  assert.equal((await action(app.baseUrl, cookie, order.id, "ready", {}, { csrf: "forged" })).status, 403);
  assert.equal(orderStatus(app, order.id).status, "ACCEPTED");
});

test("a cross-origin action is refused", async () => {
  const order = (await place(app, newLink())).body;
  const { cookie } = await signIn(app);
  const res = await action(app.baseUrl, cookie, order.id, "ready", {}, { origin: "https://evil.example" });
  assert.equal(res.status, 403);
  assert.equal(orderStatus(app, order.id).status, "ACCEPTED");
});

test("Ready is deliberate from ACCEPTED and repeating it is harmless", async () => {
  const order = (await place(app, newLink())).body;
  const { cookie } = await signIn(app);

  const ready = await action(app.baseUrl, cookie, order.id, "ready");
  assert.equal(ready.status, 200);
  assert.equal(((await ready.json()) as { status: string }).status, "READY");

  const again = await action(app.baseUrl, cookie, order.id, "ready");
  assert.equal(again.status, 200);
  assert.equal(((await again.json()) as { statusSequence: number }).statusSequence, 2);
});

test("collection needs the matching code and a wrong code leaves the order uncollected", async () => {
  const order = (await place(app, newLink())).body;
  const { cookie } = await signIn(app);
  await action(app.baseUrl, cookie, order.id, "ready");

  const wrongCode = order.pickupCode === "000000" ? "111111" : "000000";
  const wrong = await action(app.baseUrl, cookie, order.id, "collected", { pickupCode: wrongCode });
  assert.equal(wrong.status, 422);
  assert.equal(((await wrong.json()) as { code: string }).code, "PICKUP_CODE_MISMATCH");
  assert.equal(orderStatus(app, order.id).status, "READY");

  const ok = await action(app.baseUrl, cookie, order.id, "collected", { pickupCode: order.pickupCode });
  assert.equal(ok.status, 200);
  assert.equal(((await ok.json()) as { status: string }).status, "COLLECTED");
  assert.equal(orderStatus(app, order.id).status, "COLLECTED");
});

test("no-show needs proven passage, even long after the scheduled time", async () => {
  // A separate app so the moved clock does not outlive this test or expire other sessions.
  const late = await startApp({
    databasePath: temporaryDatabasePath().path,
    now: "2026-10-18T05:00:00.000Z",
    overrides: { staffSessionLifetimeMs: 12 * 3_600_000 },
  });
  try {
    const order = (await place(late, newLink())).body;
    const { cookie } = await signIn(late);
    late.setNow("2026-10-18T09:00:00.000Z"); // well past the 07:10 scheduled arrival

    for (const state of ["UNKNOWN", "AHEAD"] as const) {
      late.setPassage(state);
      // A kiosk-supplied boolean is not evidence.
      const res = await action(late.baseUrl, cookie, order.id, "no-show", { passed: true });
      assert.equal(res.status, 409, state);
      assert.equal(((await res.json()) as { code: string }).code, "TRAIN_NOT_PASSED");
      assert.equal(orderStatus(late, order.id).status, "ACCEPTED");
    }

    late.setPassage("ERROR");
    assert.equal((await action(late.baseUrl, cookie, order.id, "no-show")).status, 503);
    assert.equal(orderStatus(late, order.id).status, "ACCEPTED");

    late.setPassage("PASSED");
    const done = await action(late.baseUrl, cookie, order.id, "no-show");
    assert.equal(done.status, 200);
    assert.equal(((await done.json()) as { status: string }).status, "NO_SHOW");
    assert.equal((await action(late.baseUrl, cookie, order.id, "no-show")).status, 200, "a repeat is harmless");
  } finally {
    await late.close();
  }
});

test("conflicting terminal actions show the latest state", async () => {
  const order = (await place(app, newLink())).body;
  const { cookie } = await signIn(app);
  await action(app.baseUrl, cookie, order.id, "ready");
  await action(app.baseUrl, cookie, order.id, "collected", { pickupCode: order.pickupCode });

  app.setPassage("PASSED");
  assert.equal((await action(app.baseUrl, cookie, order.id, "no-show")).status, 409);
  assert.equal((await action(app.baseUrl, cookie, order.id, "ready")).status, 409);
  assert.equal(orderStatus(app, order.id).status, "COLLECTED");
});

test("two concurrent ready clicks both succeed without a second change", async () => {
  const order = (await place(app, newLink())).body;
  const { cookie } = await signIn(app);

  const [a, b] = await Promise.all([
    action(app.baseUrl, cookie, order.id, "ready"),
    action(app.baseUrl, cookie, order.id, "ready"),
  ]);
  assert.equal(a.status, 200);
  assert.equal(b.status, 200);
  assert.equal(orderStatus(app, order.id).status_sequence, 2);
});

test("two concurrent collections with the matching code settle on one COLLECTED", async () => {
  const order = (await place(app, newLink())).body;
  const { cookie } = await signIn(app);
  await action(app.baseUrl, cookie, order.id, "ready");

  const [a, b] = await Promise.all([
    action(app.baseUrl, cookie, order.id, "collected", { pickupCode: order.pickupCode }),
    action(app.baseUrl, cookie, order.id, "collected", { pickupCode: order.pickupCode }),
  ]);
  assert.equal(a.status, 200);
  assert.equal(b.status, 200);
  assert.equal(orderStatus(app, order.id).status, "COLLECTED");
  assert.equal(orderStatus(app, order.id).status_sequence, 3);
});

test("a session cannot act on another collection point's order", async () => {
  const peradeniya = (await place(app, newLink())).body;
  const polgahawela = await signIn(app, "polgahawela-junction");

  const denied = await action(app.baseUrl, polgahawela.cookie, peradeniya.id, "ready");
  assert.equal(denied.status, 404, "another point's order looks missing");
  assert.equal(orderStatus(app, peradeniya.id).status, "ACCEPTED");

  const owner = await signIn(app, "peradeniya-junction");
  assert.equal((await action(app.baseUrl, owner.cookie, peradeniya.id, "ready")).status, 200);
});

test("a placed Polgahawela order can only be operated by its own point", async () => {
  const order = (await placePolgahawela(app, newLink())).body;
  const peradeniya = await signIn(app, "peradeniya-junction");
  assert.equal((await action(app.baseUrl, peradeniya.cookie, order.id, "ready")).status, 404);

  const polgahawela = await signIn(app, "polgahawela-junction");
  assert.equal((await action(app.baseUrl, polgahawela.cookie, order.id, "ready")).status, 200);
});

test("an invalid state change reports 409 and never changes the order", async () => {
  const order = (await place(app, newLink())).body;
  const { cookie } = await signIn(app);
  assert.equal((await action(app.baseUrl, cookie, order.id, "collected", { pickupCode: order.pickupCode })).status, 409);
  assert.equal(orderStatus(app, order.id).status, "ACCEPTED");
});
