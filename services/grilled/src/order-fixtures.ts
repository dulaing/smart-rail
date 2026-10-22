import { randomUUID } from "node:crypto";
import { PERADENIYA_STATION_ID, POLGAHAWELA_STATION_ID } from "./seed";
import { kioskHeaders, serviceHeaders, type TestApp } from "./test-support";

export const JOURNEY_ID = "7c1d5f4e-3c41-4f0a-9d63-2f7b8d0a1111";
export const PERADENIYA_STOP = "5d2d6a52-0a56-4a06-8c33-9a4c1f0e2222";
export const POLGAHAWELA_STOP = "9b7a1c20-6d3e-4b6f-8a41-0c5d7e8f3333";

// 05:00:00Z is "now" in tests. The train reaches Peradeniya at 07:25Z, so with the default 30 minute
// cutoff ordering closes at 06:55Z. Its ETA evidence is one minute old.
export const ETA = "2026-10-18T07:25:00.000Z";
export const EVIDENCE = "2026-10-18T04:59:00.000Z";

export interface Body {
  [key: string]: unknown;
}

export function snapshot(overrides: Body = {}): Body {
  return {
    journeyId: JOURNEY_ID,
    trainNumber: "SIM-3001",
    serviceDate: "2026-10-18",
    version: 1,
    orderClosed: false,
    blockedPickupStopIds: [],
    ...overrides,
  };
}

export function peradeniyaPickup(overrides: Body = {}): Body {
  return {
    stopId: PERADENIYA_STOP,
    stationId: PERADENIYA_STATION_ID,
    scheduledArrival: "2026-10-18T07:10:00.000Z",
    estimatedArrival: ETA,
    etaEvidenceAt: EVIDENCE,
    ...overrides,
  };
}

export function polgahawelaPickup(overrides: Body = {}): Body {
  return {
    stopId: POLGAHAWELA_STOP,
    stationId: POLGAHAWELA_STATION_ID,
    scheduledArrival: "2026-10-18T06:40:00.000Z",
    estimatedArrival: "2026-10-18T06:55:00.000Z",
    etaEvidenceAt: EVIDENCE,
    ...overrides,
  };
}

export function placement(overrides: Body = {}): Body {
  return {
    collectionPointId: "peradeniya-junction",
    journey: snapshot(),
    pickup: peradeniyaPickup(),
    items: [{ menuItemId: "pdn-lamprais", quantity: 1 }],
    ...overrides,
  };
}

export async function call(
  app: TestApp,
  method: string,
  path: string,
  options: { headers?: Record<string, string>; body?: unknown; raw?: string } = {},
): Promise<{ status: number; body: any; headers: Headers }> {
  const res = await fetch(`${app.baseUrl}${path}`, {
    method,
    headers: { ...(options.body !== undefined || options.raw !== undefined ? { "Content-Type": "application/json" } : {}), ...options.headers },
    ...(options.raw !== undefined ? { body: options.raw } : options.body !== undefined ? { body: JSON.stringify(options.body) } : {}),
  });
  const text = await res.text();
  return { status: res.status, body: text ? JSON.parse(text) : undefined, headers: res.headers };
}

export const place = (app: TestApp, linkId: string, body: unknown = placement()) =>
  call(app, "PUT", `/v1/order-links/${linkId}`, { headers: serviceHeaders(), body });

export const lookup = (app: TestApp, linkId: string) => call(app, "GET", `/v1/order-links/${linkId}`, { headers: serviceHeaders() });

export const cancel = (app: TestApp, linkId: string, body: unknown = { journey: snapshot(), pickup: peradeniyaPickup() }) =>
  call(app, "POST", `/v1/order-links/${linkId}/cancellation`, { headers: serviceHeaders(), body });

export const kiosk = (app: TestApp, point: string, method: string, suffix: string, body?: unknown) =>
  call(app, method, `/v1/kiosk/collection-points/${point}/orders${suffix}`, {
    headers: kioskHeaders(point),
    ...(body === undefined ? {} : { body }),
  });

export const newLink = (): string => randomUUID();

export function stockOf(app: TestApp, menuItemId: string): number {
  return (app.db.prepare("SELECT stock_quantity FROM menu_items WHERE id = ?").get(menuItemId) as { stock_quantity: number }).stock_quantity;
}

export function count(app: TestApp, table: string): number {
  return (app.db.prepare(`SELECT count(*) n FROM ${table}`).get() as { n: number }).n;
}

// A confirmed train change as SmartRail delivers it: full eligibility plus every stop's pickup time.
export function journeyEvent(overrides: Body = {}): Body {
  return {
    eventId: `train-change:${randomUUID()}`,
    eventType: "DELAY",
    journey: snapshot({ version: 2 }),
    stops: [polgahawelaPickup(), peradeniyaPickup()],
    calculatedAt: "2026-10-18T05:00:00.000Z",
    occurredAt: "2026-10-18T05:00:00.000Z",
    ...overrides,
  };
}

export const deliver = (app: TestApp, body: unknown) =>
  call(app, "POST", "/v1/journey-events", { headers: serviceHeaders(), body });

export const placePolgahawela = (app: TestApp, linkId: string) =>
  place(app, linkId, placement({ collectionPointId: "polgahawela-junction", pickup: polgahawelaPickup(), items: [{ menuItemId: "plg-plain-tea", quantity: 1 }] }));
