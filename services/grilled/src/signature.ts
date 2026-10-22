import { createHmac, timingSafeEqual } from "node:crypto";

export const SIGNATURE_HEADER = "X-Grilled-Signature";
export const TIMESTAMP_HEADER = "X-Grilled-Timestamp";
export const EVENT_ID_HEADER = "X-Grilled-Event-Id";

// The signed text is the timestamp, one full stop, then the exact bytes of the request body. The
// body is signed as it is sent, never re-serialised, so the receiver can check it from the raw bytes.
export function sign(secret: string, timestampSeconds: number, rawBody: string): string {
  return `v1=${createHmac("sha256", secret).update(`${timestampSeconds}.`).update(rawBody, "utf8").digest("hex")}`;
}

// For tests and the shared fixtures: the same comparison the receiver in SmartRail makes.
export function verify(secret: string, timestampSeconds: number, rawBody: string, signature: string): boolean {
  const expected = Buffer.from(sign(secret, timestampSeconds, rawBody));
  const provided = Buffer.from(signature);
  return expected.length === provided.length && timingSafeEqual(expected, provided);
}
