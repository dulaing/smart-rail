import assert from "node:assert/strict";
import type { AddressInfo } from "node:net";
import { test } from "node:test";
import { openDatabase } from "./db";
import { migrate } from "./migrations";
import { createApp } from "./server";
import { createTestClock } from "./test-clock";
import { SERVICE_KEY, temporaryDatabasePath } from "./test-support";

async function withApp(testClock: ReturnType<typeof createTestClock> | undefined, run: (baseUrl: string) => Promise<void>) {
  const path = temporaryDatabasePath();
  const db = openDatabase(path.path);
  migrate(db);
  const config = { port: 0, databasePath: path.path, serviceKey: SERVICE_KEY, kioskKeys: new Map(), smartRailBaseUrl: undefined, smartRailServiceKey: undefined, callbackSecret: undefined, callbackPollMs: 0, callbackMaxBackoffSeconds: 60, callbackFailedRetrySeconds: 600 };
  const server = createApp({ config, db, ...(testClock ? { testClock } : {}) });
  await new Promise<void>((resolve) => server.listen(0, resolve));
  try {
    await run(`http://localhost:${(server.address() as AddressInfo).port}`);
  } finally {
    await new Promise<void>((resolve) => {
      server.close(() => resolve());
      server.closeAllConnections();
    });
    db.close();
    path.remove();
  }
}

const put = (baseUrl: string, body: unknown, key = SERVICE_KEY) =>
  fetch(`${baseUrl}/internal/test/clock`, { method: "PUT", headers: { "X-RailBites-Service-Key": key }, body: JSON.stringify(body) });

test("the clock route exists only when a test clock is supplied", async () => {
  await withApp(undefined, async (baseUrl) => {
    assert.equal((await put(baseUrl, { now: "2026-10-18T05:00:00.000Z" })).status, 404);
  });
});

test("a test clock pins the instant, needs the service key, and can return to real time", async () => {
  const clock = createTestClock();
  await withApp(clock, async (baseUrl) => {
    assert.equal((await put(baseUrl, { now: "2026-10-18T05:00:00.000Z" }, "wrong")).status, 401);
    assert.equal((await put(baseUrl, { now: "yesterday" })).status, 400);
    assert.equal((await put(baseUrl, {})).status, 400);

    const pinned = await put(baseUrl, { now: "2026-10-18T05:00:00.000Z" });
    assert.equal(pinned.status, 200);
    assert.deepEqual(await pinned.json(), { now: "2026-10-18T05:00:00.000Z" });
    assert.equal(clock.now().toISOString(), "2026-10-18T05:00:00.000Z");

    await put(baseUrl, { now: null });
    assert.ok(Math.abs(clock.now().getTime() - Date.now()) < 5_000);
  });
});
