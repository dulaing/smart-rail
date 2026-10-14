import assert from "node:assert/strict";
import type { AddressInfo } from "node:net";
import { after, before, test } from "node:test";
import { createApp } from "./server";

const server = createApp();
let baseUrl = "";

before(async () => {
  await new Promise<void>((resolve) => server.listen(0, resolve));
  baseUrl = `http://localhost:${(server.address() as AddressInfo).port}`;
});

after(() => {
  server.close();
});

test("GET /health/live returns 200 Healthy", async () => {
  const res = await fetch(`${baseUrl}/health/live`);
  assert.equal(res.status, 200);
  assert.deepEqual(await res.json(), { status: "Healthy" });
});

test("unknown route returns 404", async () => {
  const res = await fetch(`${baseUrl}/nope`);
  assert.equal(res.status, 404);
});
