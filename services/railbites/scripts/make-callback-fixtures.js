// Regenerates fixtures/signed-callbacks.json: callback bodies signed with a fixed secret at a fixed
// time. Both services check the same file, so a change to the signing rule shows up in both test
// suites. Run: node scripts/make-callback-fixtures.js
const { createHmac } = require("node:crypto");
const { writeFileSync, mkdirSync } = require("node:fs");
const { join } = require("node:path");

const secret = "fixture-callback-secret";
const now = "2026-10-18T05:00:00.000Z";
const nowSeconds = Date.parse(now) / 1000;
const orderLinkId = "3f6c0f1e-8f4e-4a6e-9a43-7d1b2c3d4e5f";

const update = (eventId, status, statusSequence, estimatedArrival = "2026-10-18T07:25:00.000Z") =>
  JSON.stringify({
    eventId,
    orderLinkId,
    providerOrderRef: "ord_fixture",
    status,
    statusSequence,
    reasonCode: null,
    pickupTime: {
      scheduledArrival: "2026-10-18T07:10:00.000Z",
      estimatedArrival,
      etaEvidenceAt: estimatedArrival === null ? null : "2026-10-18T04:59:00.000Z",
    },
    occurredAt: now,
  });

const sign = (timestamp, body, key = secret) => `v1=${createHmac("sha256", key).update(`${timestamp}.${body}`).digest("hex")}`;

const ready = update("ord_fixture-2", "READY", 2);
const collected = update("ord_fixture-3", "COLLECTED", 3);
const tampered = ready.replace('"statusSequence":2', '"statusSequence":9');

const cases = [
  { name: "valid READY at sequence 2", timestamp: nowSeconds, body: ready, signature: sign(nowSeconds, ready), accepted: true },
  { name: "valid COLLECTED at sequence 3", timestamp: nowSeconds, body: collected, signature: sign(nowSeconds, collected), accepted: true },
  { name: "valid at the oldest accepted timestamp (300 seconds old)", timestamp: nowSeconds - 300, body: ready, signature: sign(nowSeconds - 300, ready), accepted: true },
  { name: "valid at the newest accepted timestamp (300 seconds ahead)", timestamp: nowSeconds + 300, body: ready, signature: sign(nowSeconds + 300, ready), accepted: true },
  { name: "body changed after signing", timestamp: nowSeconds, body: tampered, signature: sign(nowSeconds, ready), accepted: false },
  { name: "signed with another secret", timestamp: nowSeconds, body: ready, signature: sign(nowSeconds, ready, "another-secret"), accepted: false },
  { name: "timestamp changed after signing", timestamp: nowSeconds + 1, body: ready, signature: sign(nowSeconds, ready), accepted: false },
  { name: "expired: signed correctly but 301 seconds old", timestamp: nowSeconds - 301, body: ready, signature: sign(nowSeconds - 301, ready), accepted: false },
  { name: "too far ahead: signed correctly but 301 seconds in the future", timestamp: nowSeconds + 301, body: ready, signature: sign(nowSeconds + 301, ready), accepted: false },
  { name: "unknown signature version", timestamp: nowSeconds, body: ready, signature: sign(nowSeconds, ready).replace("v1=", "v2="), accepted: false },
  { name: "signature without a version", timestamp: nowSeconds, body: ready, signature: sign(nowSeconds, ready).slice(3), accepted: false },
];

mkdirSync(join(__dirname, "..", "fixtures"), { recursive: true });
writeFileSync(
  join(__dirname, "..", "fixtures", "signed-callbacks.json"),
  `${JSON.stringify({ secret, now, orderLinkId, signed: "HMAC-SHA256 over '<timestamp>.<raw body>', hex, prefixed v1=", cases }, null, 2)}\n`,
);
