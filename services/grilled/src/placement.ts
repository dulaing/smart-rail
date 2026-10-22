import { createHash, randomInt, randomUUID } from "node:crypto";
import type { Db } from "./db";
import { immediate } from "./db";
import { ApiError, type Reply } from "./http";
import {
  applyEligibility,
  isServed,
  parsePickup,
  parseSnapshot,
  type Eligibility,
  type JourneySnapshot,
  type Pickup,
} from "./journey";
import { advance, enqueueUpdate, findByLink, isOpen, orderView, type OrderRow } from "./orders";
import * as parse from "./parse";

// An ETA is usable while its evidence is younger than this; it matches SmartRail's "unknown" threshold.
const EVIDENCE_LIMIT_MS = 240_000;
const EVIDENCE_CLOCK_SKEW_MS = 60_000;
const MAX_ITEMS = 20;
const MAX_QUANTITY = 20;

interface PlaceOrderRequest {
  collectionPointId: string;
  journey: JourneySnapshot;
  pickup: Pickup;
  items: { menuItemId: string; quantity: number }[];
}

function parsePlaceOrder(value: unknown): PlaceOrderRequest {
  const body = parse.object(value, "body");
  const journey = parseSnapshot(body["journey"], "journey");
  if (journey.trainNumber === undefined) throw parse.invalid("journey.trainNumber", "is required");
  if (journey.serviceDate === undefined) throw parse.invalid("journey.serviceDate", "is required");

  const seen = new Set<string>();
  const items = parse.array(body["items"], "items", MAX_ITEMS).map((entry, index) => {
    const item = parse.object(entry, `items[${index}]`);
    const menuItemId = parse.text(item["menuItemId"], `items[${index}].menuItemId`);
    if (seen.has(menuItemId)) throw parse.invalid(`items[${index}].menuItemId`, "appears twice; combine the quantities");
    seen.add(menuItemId);
    return { menuItemId, quantity: parse.integer(item["quantity"], `items[${index}].quantity`, 1, MAX_QUANTITY) };
  });
  if (items.length === 0) throw parse.invalid("items", "must contain at least one item");

  return {
    collectionPointId: parse.text(body["collectionPointId"], "collectionPointId"),
    journey,
    pickup: parsePickup(body["pickup"], "pickup"),
    items,
  };
}

// What the request asks for, not what the train looked like when it was sent: items, journey, stop and
// collection point. A retry may carry a fresher ETA or journey version and still match.
function digest(request: PlaceOrderRequest): string {
  const items = request.items.map((i) => [i.menuItemId, i.quantity] as const).sort(([a], [b]) => (a < b ? -1 : 1));
  return createHash("sha256")
    .update(JSON.stringify([request.collectionPointId, request.journey.journeyId, request.pickup.stopId, items]))
    .digest("hex");
}

function pickupCode(db: Db): string {
  for (;;) {
    const code = String(randomInt(0, 1_000_000)).padStart(6, "0");
    if (!db.prepare("SELECT 1 FROM orders WHERE pickup_code = ?").get(code)) return code;
  }
}

// ETA usability shared by placement and cancellation. Returns the reason it is unusable, if any.
function etaProblem(pickup: Pickup, now: Date): "TRAIN_UNKNOWN" | null {
  if (pickup.estimatedArrival === null || pickup.etaEvidenceAt === null) return "TRAIN_UNKNOWN";
  return now.getTime() - Date.parse(pickup.etaEvidenceAt) >= EVIDENCE_LIMIT_MS ? "TRAIN_UNKNOWN" : null;
}

function checkEvidenceNotFuture(pickup: Pickup, now: Date): void {
  if (pickup.etaEvidenceAt !== null && Date.parse(pickup.etaEvidenceAt) > now.getTime() + EVIDENCE_CLOCK_SKEW_MS) {
    throw parse.invalid("pickup.etaEvidenceAt", "is in the future");
  }
}

// Equality is closed: an order is allowed only while now is strictly before ETA minus the cutoff.
const cutoffPassed = (pickup: Pickup, cutoffMinutes: number, now: Date): boolean =>
  now.getTime() >= Date.parse(pickup.estimatedArrival!) - cutoffMinutes * 60_000;

type Outcome = { reply: Reply } | { rejection: ApiError };

// A rejection is returned, not thrown, so that a newer journey restriction learned along the way
// (and the orders it cancelled) still commits.
function settle(outcome: Outcome): Reply {
  if ("rejection" in outcome) throw outcome.rejection;
  return outcome.reply;
}

const reject = (status: number, code: string, message: string, details?: unknown): Outcome => ({
  rejection: new ApiError(status, code, message, details),
});

export function placeOrder(db: Db, now: Date, orderLinkId: string, body: unknown): Reply {
  const request = parsePlaceOrder(body);
  checkEvidenceNotFuture(request.pickup, now);
  const requestDigest = digest(request);

  return settle(
    immediate(db, (): Outcome => {
      // A repeated link ID returns the order's current state before any new-order check.
      const existing = findByLink(db, orderLinkId);
      if (existing) {
        if (existing.request_digest !== requestDigest) {
          return reject(409, "IDEMPOTENCY_CONFLICT", "This order link was already used for a different order.");
        }
        return { reply: { status: 200, body: orderView(db, existing) } };
      }

      const point = db.prepare("SELECT * FROM collection_points WHERE id = ? AND active = 1").get(request.collectionPointId) as unknown as
        | { id: string; smart_rail_station_id: string; cutoff_minutes: number }
        | undefined;
      if (!point) return reject(404, "COLLECTION_POINT_NOT_FOUND", "Unknown collection point.");
      if (point.smart_rail_station_id !== request.pickup.stationId) {
        return reject(422, "PICKUP_MISMATCH", "The pickup station does not belong to this collection point.");
      }

      const eligibility = applyEligibility(db, request.journey, now);
      const refusal = refuseNewOrder(eligibility, request, point.cutoff_minutes, now);
      if (refusal) return refusal;

      const lines = [];
      for (const item of request.items) {
        const menu = db
          .prepare("SELECT id, name, price_minor_lkr, stock_quantity FROM menu_items WHERE id = ? AND collection_point_id = ? AND active = 1")
          .get(item.menuItemId, point.id) as unknown as
          | { id: string; name: string; price_minor_lkr: number; stock_quantity: number }
          | undefined;
        if (!menu) return reject(422, "ITEM_UNAVAILABLE", "An item is not on this menu.", { menuItemId: item.menuItemId });
        if (menu.stock_quantity < item.quantity) {
          return reject(422, "STOCK_INSUFFICIENT", "Not enough stock.", {
            menuItemId: item.menuItemId,
            available: menu.stock_quantity,
          });
        }
        lines.push({ menu, quantity: item.quantity });
      }

      const orderId = `ord_${randomUUID()}`;
      const at = now.toISOString();
      db.prepare(
        `INSERT INTO orders (id, order_link_id, request_digest, journey_id, train_number, service_date, pickup_stop_id,
           collection_point_id, pickup_code, status, status_sequence, scheduled_arrival, current_eta, eta_evidence_at,
           pickup_version, created_at, updated_at)
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 'ACCEPTED', 1, ?, ?, ?, ?, ?, ?)`,
      ).run(
        orderId,
        orderLinkId,
        requestDigest,
        request.journey.journeyId,
        request.journey.trainNumber!,
        request.journey.serviceDate!,
        request.pickup.stopId,
        point.id,
        pickupCode(db),
        request.pickup.scheduledArrival,
        request.pickup.estimatedArrival,
        request.pickup.etaEvidenceAt,
        request.journey.version,
        at,
        at,
      );
      for (const { menu, quantity } of lines) {
        db.prepare("UPDATE menu_items SET stock_quantity = stock_quantity - ? WHERE id = ?").run(quantity, menu.id);
        db.prepare(
          "INSERT INTO order_items (id, order_id, menu_item_id, name, quantity, unit_price_minor_lkr) VALUES (?, ?, ?, ?, ?, ?)",
        ).run(randomUUID(), orderId, menu.id, menu.name, quantity, menu.price_minor_lkr);
      }

      const created = db.prepare("SELECT * FROM orders WHERE id = ?").get(orderId) as unknown as OrderRow;
      enqueueUpdate(db, created, now);
      return { reply: { status: 201, body: orderView(db, created) } };
    }),
  );
}

function refuseNewOrder(
  eligibility: Eligibility,
  request: PlaceOrderRequest,
  cutoffMinutes: number,
  now: Date,
): Outcome | null {
  if (!isServed(eligibility, request.pickup.stopId)) {
    return reject(422, "STOP_NOT_SERVED", "The train no longer serves this pickup stop.");
  }
  if (etaProblem(request.pickup, now)) {
    return reject(422, "TRAIN_UNKNOWN", "The train's arrival time is unknown.");
  }
  if (cutoffPassed(request.pickup, cutoffMinutes, now)) {
    return reject(422, "CUTOFF_PASSED", "The ordering window for this pickup has closed.", { cutoffMinutes });
  }
  return null;
}

export function getOrderByLink(db: Db, orderLinkId: string): Reply {
  const order = findByLink(db, orderLinkId);
  if (!order) throw new ApiError(404, "ORDER_NOT_FOUND", "No order for this link.");
  return { status: 200, body: orderView(db, order) };
}

export function cancelOrder(db: Db, now: Date, orderLinkId: string, body: unknown): Reply {
  const payload = parse.object(body, "body");
  const journey = parseSnapshot(payload["journey"], "journey");
  const pickup = parsePickup(payload["pickup"], "pickup");
  checkEvidenceNotFuture(pickup, now);

  return settle(
    immediate(db, (): Outcome => {
      const order = findByLink(db, orderLinkId);
      if (!order) return reject(404, "ORDER_NOT_FOUND", "No order for this link.");
      if (journey.journeyId !== order.journey_id || pickup.stopId !== order.pickup_stop_id) {
        return reject(422, "PICKUP_MISMATCH", "The snapshot is not for this order's journey and stop.");
      }

      // Repeating a cancellation is harmless; a collected or no-show order cannot be undone.
      if (order.status === "CANCELLED") return { reply: { status: 200, body: orderView(db, order) } };
      if (!isOpen(order.status)) {
        return reject(409, "INVALID_STATE", `A ${order.status} order cannot be cancelled.`);
      }

      // Learn any newer restriction first; it may already cancel this order for the train's sake.
      applyEligibility(db, journey, now);
      const afterRestriction = findByLink(db, orderLinkId)!;
      if (afterRestriction.status === "CANCELLED") {
        return { reply: { status: 200, body: orderView(db, afterRestriction) } };
      }

      if (etaProblem(pickup, now)) {
        return reject(409, "TRAIN_UNKNOWN", "The cutoff cannot be confirmed while the arrival time is unknown.");
      }
      const point = db.prepare("SELECT cutoff_minutes FROM collection_points WHERE id = ?").get(order.collection_point_id) as unknown as {
        cutoff_minutes: number;
      };
      if (cutoffPassed(pickup, point.cutoff_minutes, now)) {
        return reject(409, "CUTOFF_PASSED", "The cancellation window for this order has closed.", {
          cutoffMinutes: point.cutoff_minutes,
        });
      }

      const cancelled = advance(db, afterRestriction, { status: "CANCELLED", cancellationReason: "PASSENGER_CANCELLED" }, now);
      return { reply: { status: 200, body: orderView(db, cancelled) } };
    }),
  );
}
