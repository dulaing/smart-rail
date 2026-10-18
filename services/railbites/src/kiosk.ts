import { timingSafeEqual } from "node:crypto";
import type { Db } from "./db";
import { immediate } from "./db";
import { ApiError, type Reply } from "./http";
import { advance, findById, isOpen, kioskView, type OrderRow, type OrderStatus } from "./orders";
import * as parse from "./parse";
import type { PassageReader } from "./smartrail";

const STATUSES: OrderStatus[] = ["ACCEPTED", "READY", "COLLECTED", "NO_SHOW", "CANCELLED"];

// Orders are always looked up inside the kiosk's own collection point, so another point's order
// looks exactly like a missing one.
function ownOrder(db: Db, collectionPointId: string, orderId: string): OrderRow {
  const order = findById(db, orderId);
  if (!order || order.collection_point_id !== collectionPointId) {
    throw new ApiError(404, "ORDER_NOT_FOUND", "No such order at this collection point.");
  }
  return order;
}

const invalidState = (order: OrderRow, action: string) =>
  new ApiError(409, "INVALID_STATE", `A ${order.status} order cannot be ${action}.`);

export function listOrders(db: Db, collectionPointId: string, status: string | null): Reply {
  if (status !== null && !STATUSES.includes(status as OrderStatus)) throw parse.invalid("status", "is not an order status");
  const rows = (
    status === null
      ? db.prepare("SELECT * FROM orders WHERE collection_point_id = ? ORDER BY COALESCE(current_eta, scheduled_arrival), created_at")
          .all(collectionPointId)
      : db
          .prepare("SELECT * FROM orders WHERE collection_point_id = ? AND status = ? ORDER BY COALESCE(current_eta, scheduled_arrival), created_at")
          .all(collectionPointId, status)
  ) as unknown as OrderRow[];
  return { status: 200, body: { orders: rows.map((row) => kioskView(db, row)) } };
}

export function markReady(db: Db, now: Date, collectionPointId: string, orderId: string): Reply {
  return immediate(db, () => {
    const order = ownOrder(db, collectionPointId, orderId);
    if (order.status === "READY") return { status: 200, body: kioskView(db, order) };
    if (order.status !== "ACCEPTED") throw invalidState(order, "marked ready");
    return { status: 200, body: kioskView(db, advance(db, order, { status: "READY" }, now)) };
  });
}

function sameCode(expected: string, provided: string): boolean {
  const a = Buffer.from(expected);
  const b = Buffer.from(provided);
  return a.length === b.length && timingSafeEqual(a, b);
}

export function markCollected(db: Db, now: Date, collectionPointId: string, orderId: string, body: unknown): Reply {
  const pickupCode = parse.text(parse.object(body, "body")["pickupCode"], "pickupCode");
  return immediate(db, () => {
    const order = ownOrder(db, collectionPointId, orderId);
    if (order.status !== "READY" && order.status !== "COLLECTED") throw invalidState(order, "collected");
    if (!sameCode(order.pickup_code, pickupCode)) {
      throw new ApiError(422, "PICKUP_CODE_MISMATCH", "The pickup code does not match this order.");
    }
    if (order.status === "COLLECTED") return { status: 200, body: kioskView(db, order) };
    return { status: 200, body: kioskView(db, advance(db, order, { status: "COLLECTED" }, now)) };
  });
}

// No-show is allowed only when SmartRail itself confirms the train has passed the pickup stop. The
// read happens outside the write transaction (it is a network call), then the order is rechecked.
export async function markNoShow(
  db: Db,
  now: () => Date,
  passage: PassageReader,
  collectionPointId: string,
  orderId: string,
): Promise<Reply> {
  const order = ownOrder(db, collectionPointId, orderId);
  if (order.status === "NO_SHOW") return { status: 200, body: kioskView(db, order) };
  if (!isOpen(order.status)) throw invalidState(order, "marked no-show");

  let state;
  try {
    state = await passage.read(order.journey_id, order.pickup_stop_id);
  } catch {
    throw new ApiError(503, "SMARTRAIL_UNAVAILABLE", "Could not confirm the train's position. Try again shortly.");
  }
  if (state !== "PASSED") {
    throw new ApiError(409, "TRAIN_NOT_PASSED", "SmartRail does not confirm the train has passed this stop.", { state });
  }

  return immediate(db, () => {
    const current = ownOrder(db, collectionPointId, orderId);
    if (current.status === "NO_SHOW") return { status: 200, body: kioskView(db, current) };
    if (!isOpen(current.status)) throw invalidState(current, "marked no-show");
    return { status: 200, body: kioskView(db, advance(db, current, { status: "NO_SHOW" }, now())) };
  });
}
