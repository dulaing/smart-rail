import { randomBytes, scryptSync, timingSafeEqual } from "node:crypto";

// A shared staff login is one password per collection point. We store only a salted hash, never the
// password: `scrypt$<N>$<r>$<p>$<salt>$<hash>` with the salt and hash in URL-safe base64. The `$`
// separator and base64url keep the value free of `=`, so `point=hash` configuration splits cleanly.
const SCRYPT_N = 16384;
const SCRYPT_R = 8;
const SCRYPT_P = 1;
const SALT_BYTES = 16;
const HASH_BYTES = 32;

// Hash a password for provisioning. The caller must not print the password it passes in.
export function hashPassword(password: string): string {
  const salt = randomBytes(SALT_BYTES);
  const derived = scryptSync(password, salt, HASH_BYTES, { N: SCRYPT_N, r: SCRYPT_R, p: SCRYPT_P });
  return ["scrypt", SCRYPT_N, SCRYPT_R, SCRYPT_P, salt.toString("base64url"), derived.toString("base64url")].join("$");
}

function validParams(N: number, r: number, p: number): boolean {
  return Number.isInteger(N) && Number.isInteger(r) && Number.isInteger(p) && N >= 2 && N <= 1 << 20 && r >= 1 && r <= 32 && p >= 1 && p <= 16;
}

// Verify a password against a stored hash in constant time. A malformed hash never matches.
export function verifyPassword(password: string, stored: string): boolean {
  const parts = stored.split("$");
  if (parts.length !== 6 || parts[0] !== "scrypt") return false;
  const N = Number(parts[1]);
  const r = Number(parts[2]);
  const p = Number(parts[3]);
  if (!validParams(N, r, p)) return false;

  let salt: Buffer;
  let expected: Buffer;
  try {
    salt = Buffer.from(parts[4]!, "base64url");
    expected = Buffer.from(parts[5]!, "base64url");
  } catch {
    return false;
  }
  if (salt.length === 0 || expected.length === 0) return false;

  let actual: Buffer;
  try {
    actual = scryptSync(password, salt, expected.length, { N, r, p });
  } catch {
    return false;
  }
  return actual.length === expected.length && timingSafeEqual(actual, expected);
}

// Compare two opaque tokens (session ids, CSRF tokens) without leaking length or content through time.
export function sameToken(a: string, b: string): boolean {
  const da = Buffer.from(a);
  const db = Buffer.from(b);
  return da.length === db.length && timingSafeEqual(da, db);
}
