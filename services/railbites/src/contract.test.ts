import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { parse } from "yaml";

interface Operation {
  operationId?: string;
  security?: unknown[];
}

const contract = parse(readFileSync(join(__dirname, "..", "openapi.yaml"), "utf8")) as {
  openapi: string;
  paths: Record<string, Record<string, Operation>>;
  components: Record<string, Record<string, unknown>>;
};

const methods = ["get", "put", "post", "delete", "patch"];

function operations(): { path: string; method: string; operation: Operation }[] {
  return Object.entries(contract.paths).flatMap(([path, item]) =>
    methods.filter((method) => item[method]).map((method) => ({ path, method, operation: item[method]! })),
  );
}

test("the contract is OpenAPI 3.1 with a unique operation ID on every operation", () => {
  assert.equal(contract.openapi, "3.1.0");
  const ids = operations().map((o) => o.operation.operationId);
  assert.ok(ids.every((id) => typeof id === "string" && id.length > 0));
  assert.equal(new Set(ids).size, ids.length);
});

test("every $ref in the contract resolves", () => {
  const text = readFileSync(join(__dirname, "..", "openapi.yaml"), "utf8");
  const refs = [...text.matchAll(/\$ref:\s*"#\/components\/(\w+)\/(\w+)"/g)];
  assert.ok(refs.length > 20);
  for (const [, section, name] of refs) {
    assert.ok(contract.components[section!]?.[name!], `unresolved ${section}/${name}`);
  }
});

test("the contract covers the operations the boundary needs", () => {
  const present = new Set(operations().map((o) => `${o.method.toUpperCase()} ${o.path}`));
  for (const required of [
    "GET /v1/collection-points",
    "GET /v1/collection-points/{collectionPointId}/menu",
    "PUT /v1/order-links/{orderLinkId}",
    "GET /v1/order-links/{orderLinkId}",
    "POST /v1/order-links/{orderLinkId}/cancellation",
    "POST /v1/journey-events",
    "GET /v1/kiosk/collection-points/{collectionPointId}/orders",
    "POST /v1/kiosk/collection-points/{collectionPointId}/orders/{orderId}/ready",
    "POST /v1/kiosk/collection-points/{collectionPointId}/orders/{orderId}/collected",
    "POST /v1/kiosk/collection-points/{collectionPointId}/orders/{orderId}/no-show",
    "POST /internal/catering/order-updates",
  ]) {
    assert.ok(present.has(required), `missing ${required}`);
  }
});

test("the kiosk order schema stays secret-free and carries train identity", () => {
  const kiosk = contract.components["schemas"]!["KioskOrder"] as {
    required?: string[];
    properties?: Record<string, unknown>;
  };
  assert.ok(kiosk.required?.includes("trainNumber"));
  assert.ok(kiosk.required?.includes("serviceDate"));
  assert.ok(kiosk.required?.includes("pickupStopId"));
  assert.ok(!("pickupCode" in (kiosk.properties ?? {})), "the kiosk schema must never expose the pickup code");
});

test("only the health routes and the signed callback skip a credential", () => {
  const open = operations()
    .filter((o) => Array.isArray(o.operation.security) && o.operation.security.length === 0)
    .map((o) => o.path)
    .sort();
  assert.deepEqual(open, ["/health/live", "/health/ready", "/internal/catering/order-updates"]);
});
