import type { IncomingMessage } from "node:http";
import { ApiError } from "./http";

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,3})?Z$/;
const DATE = /^\d{4}-\d{2}-\d{2}$/;

export function isUuid(value: string): boolean {
  return UUID.test(value);
}

export function invalid(field: string, problem: string): ApiError {
  return new ApiError(400, "INVALID_REQUEST", `${field}: ${problem}`, { field });
}

export type Json = Record<string, unknown>;

export function object(value: unknown, field: string): Json {
  if (typeof value !== "object" || value === null || Array.isArray(value)) throw invalid(field, "must be an object");
  return value as Json;
}

export function text(value: unknown, field: string): string {
  if (typeof value !== "string" || value.trim() === "" || value.length > 200) throw invalid(field, "must be a non-empty string");
  return value;
}

export function uuid(value: unknown, field: string): string {
  const candidate = text(value, field);
  if (!UUID.test(candidate)) throw invalid(field, "must be a UUID");
  return candidate.toLowerCase();
}

export function integer(value: unknown, field: string, min: number, max = Number.MAX_SAFE_INTEGER): number {
  if (typeof value !== "number" || !Number.isInteger(value) || value < min || value > max) {
    throw invalid(field, `must be an integer from ${min}`);
  }
  return value;
}

export function boolean(value: unknown, field: string): boolean {
  if (typeof value !== "boolean") throw invalid(field, "must be true or false");
  return value;
}

export function date(value: unknown, field: string): string {
  const candidate = text(value, field);
  if (!DATE.test(candidate) || Number.isNaN(Date.parse(candidate))) throw invalid(field, "must be a date (YYYY-MM-DD)");
  return candidate;
}

// Returns the instant in the canonical stored form (UTC, milliseconds).
export function instant(value: unknown, field: string): string {
  const candidate = text(value, field);
  const parsed = Date.parse(candidate);
  if (!INSTANT.test(candidate) || Number.isNaN(parsed)) throw invalid(field, "must be a UTC instant (…Z)");
  return new Date(parsed).toISOString();
}

export function nullableInstant(value: unknown, field: string): string | null {
  return value === null ? null : instant(value, field);
}

export function array(value: unknown, field: string, max = 100): unknown[] {
  if (!Array.isArray(value) || value.length > max) throw invalid(field, `must be an array of at most ${max}`);
  return value;
}

const MAX_BODY_BYTES = 64 * 1024;

// Bounded, so a client cannot make the service buffer an unlimited body.
export async function readJson(req: IncomingMessage): Promise<unknown> {
  const chunks: Buffer[] = [];
  let size = 0;
  for await (const chunk of req) {
    size += (chunk as Buffer).length;
    if (size > MAX_BODY_BYTES) throw new ApiError(413, "PAYLOAD_TOO_LARGE", "Request body is too large.");
    chunks.push(chunk as Buffer);
  }
  try {
    return JSON.parse(Buffer.concat(chunks).toString("utf8"));
  } catch {
    throw new ApiError(400, "INVALID_REQUEST", "Body must be valid JSON.");
  }
}
