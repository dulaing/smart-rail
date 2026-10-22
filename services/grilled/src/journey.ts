import type { Db } from "./db";
import { ApiError } from "./http";
import { advance, isOpen, type CancellationReason, type OrderRow } from "./orders";
import * as parse from "./parse";

export interface JourneySnapshot {
  journeyId: string;
  trainNumber: string | undefined;
  serviceDate: string | undefined;
  version: number;
  orderClosed: boolean;
  blockedPickupStopIds: string[];
}

export interface Pickup {
  stopId: string;
  stationId: string;
  scheduledArrival: string;
  estimatedArrival: string | null;
  etaEvidenceAt: string | null;
}

export function parseSnapshot(value: unknown, field: string): JourneySnapshot {
  const body = parse.object(value, field);
  const blocked = parse
    .array(body["blockedPickupStopIds"], `${field}.blockedPickupStopIds`, 200)
    .map((id, index) => parse.uuid(id, `${field}.blockedPickupStopIds[${index}]`));
  return {
    journeyId: parse.uuid(body["journeyId"], `${field}.journeyId`),
    trainNumber: body["trainNumber"] === undefined ? undefined : parse.text(body["trainNumber"], `${field}.trainNumber`),
    serviceDate: body["serviceDate"] === undefined ? undefined : parse.date(body["serviceDate"], `${field}.serviceDate`),
    version: parse.integer(body["version"], `${field}.version`, 0),
    orderClosed: parse.boolean(body["orderClosed"], `${field}.orderClosed`),
    blockedPickupStopIds: [...new Set(blocked)].sort(),
  };
}

export function parsePickup(value: unknown, field: string): Pickup {
  const body = parse.object(value, field);
  const estimatedArrival = parse.nullableInstant(body["estimatedArrival"], `${field}.estimatedArrival`);
  const etaEvidenceAt = parse.nullableInstant(body["etaEvidenceAt"], `${field}.etaEvidenceAt`);
  if ((estimatedArrival === null) !== (etaEvidenceAt === null)) {
    throw parse.invalid(field, "estimatedArrival and etaEvidenceAt must both be set or both be null");
  }
  return {
    stopId: parse.uuid(body["stopId"], `${field}.stopId`),
    stationId: parse.uuid(body["stationId"], `${field}.stationId`),
    scheduledArrival: parse.instant(body["scheduledArrival"], `${field}.scheduledArrival`),
    estimatedArrival,
    etaEvidenceAt,
  };
}

export interface Eligibility {
  version: number;
  orderClosed: boolean;
  blockedPickupStopIds: string[];
}

interface StateRow {
  applied_journey_version: number;
  order_closed: number;
  blocked_pickup_stop_ids: string;
}

function readState(db: Db, journeyId: string): Eligibility | undefined {
  const row = db.prepare("SELECT * FROM journey_order_states WHERE journey_id = ?").get(journeyId) as unknown as
    | StateRow
    | undefined;
  return row && {
    version: row.applied_journey_version,
    orderClosed: row.order_closed === 1,
    blockedPickupStopIds: JSON.parse(row.blocked_pickup_stop_ids) as string[],
  };
}

export const isServed = (state: Eligibility, stopId: string): boolean =>
  !state.orderClosed && !state.blockedPickupStopIds.includes(stopId);

function sameEligibility(a: Eligibility, b: JourneySnapshot): boolean {
  return (
    a.orderClosed === b.orderClosed &&
    a.blockedPickupStopIds.length === b.blockedPickupStopIds.length &&
    a.blockedPickupStopIds.every((id, index) => id === b.blockedPickupStopIds[index])
  );
}

// Brings the provider's view of one journey up to the snapshot, or keeps its own if that is newer,
// and returns the eligibility that now rules. Newer versions replace older ones; the same version
// must agree; an older version is ignored, so a late request can never loosen a restriction the
// provider already holds. When a restriction arrives, open orders it covers are cancelled with a
// reason. A withdrawal reopens the stop for new orders but revives nothing. The caller holds the
// write transaction.
export function applyEligibility(db: Db, snapshot: JourneySnapshot, now: Date): Eligibility {
  const current = readState(db, snapshot.journeyId);

  if (current && snapshot.version < current.version) return current;
  if (current && snapshot.version === current.version) {
    if (!sameEligibility(current, snapshot)) {
      throw new ApiError(409, "JOURNEY_STATE_CONFLICT", "Journey version already applied with a different eligibility.");
    }
    return current;
  }

  const next: Eligibility = {
    version: snapshot.version,
    orderClosed: snapshot.orderClosed,
    blockedPickupStopIds: snapshot.blockedPickupStopIds,
  };
  db.prepare(
    `INSERT INTO journey_order_states (journey_id, applied_journey_version, order_closed, blocked_pickup_stop_ids, updated_at)
     VALUES (?, ?, ?, ?, ?)
     ON CONFLICT (journey_id) DO UPDATE SET
       applied_journey_version = excluded.applied_journey_version,
       order_closed = excluded.order_closed,
       blocked_pickup_stop_ids = excluded.blocked_pickup_stop_ids,
       updated_at = excluded.updated_at`,
  ).run(snapshot.journeyId, next.version, next.orderClosed ? 1 : 0, JSON.stringify(next.blockedPickupStopIds), now.toISOString());

  cancelUnserved(db, snapshot.journeyId, next, now);
  return next;
}

export function cancelUnserved(db: Db, journeyId: string, state: Eligibility, now: Date): number {
  const open = db
    .prepare("SELECT * FROM orders WHERE journey_id = ? AND status IN ('ACCEPTED', 'READY') ORDER BY created_at")
    .all(journeyId) as unknown as OrderRow[];
  let cancelled = 0;
  for (const order of open) {
    if (!isOpen(order.status) || isServed(state, order.pickup_stop_id)) continue;
    const reason: CancellationReason = state.orderClosed ? "TRAIN_CANCELLED" : "TRAIN_ENDED_EARLY";
    advance(db, order, { status: "CANCELLED", cancellationReason: reason }, now);
    cancelled += 1;
  }
  return cancelled;
}
