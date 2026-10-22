import assert from "node:assert/strict";
import { after, before, test } from "node:test";
import { startApp, temporaryDatabasePath, type TestApp } from "./test-support";

const database = temporaryDatabasePath();
let app: TestApp;

before(async () => {
  app = await startApp({ databasePath: database.path });
});

after(async () => {
  await app.close();
  database.remove();
});

test("GET /health/live returns 200 Healthy without credentials", async () => {
  const res = await fetch(`${app.baseUrl}/health/live`);
  assert.equal(res.status, 200);
  assert.deepEqual(await res.json(), { status: "Healthy" });
});

test("unknown route returns 404 in the standard error shape", async () => {
  const res = await fetch(`${app.baseUrl}/nope`);
  assert.equal(res.status, 404);
  assert.equal(((await res.json()) as { code: string }).code, "NOT_FOUND");
});

test("a known path with the wrong method returns 405", async () => {
  const res = await fetch(`${app.baseUrl}/health/live`, { method: "POST" });
  assert.equal(res.status, 405);
});
