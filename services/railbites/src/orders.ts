import type { Db } from "./db";

export type OrderStatus = "ACCEPTED" | "READY" | "COLLECTED" | "NO_SHOW" | "CANCELLED";
export type CancellationReason = "PASSENGER_CANCELLED" | "TRAIN_CANCELLED" | "TRAIN_ENDED_EARLY";

export interface OrderRow {
  id: string;
  order_link_id: string;
  request_digest: string;
  journey_id: string;
  train_number: string;
  service_date: string;
  pickup_stop_id: string;
  collection_point_id: string;
  pickup_code: string;
  status: OrderStatus;
  status_sequence: number;
  cancellation_reason: CancellationReason | null;
  scheduled_arrival: string;
  current_eta: string | null;
  eta_evidence_at: string | null;
  pickup_version: number;
  created_at: string;
  updated_at: string;
}

interface ItemRow {
  menu_item_id: string;
  name: string;
  quantity: number;
  unit_price_minor_lkr: number;
}

export const isOpen = (status: OrderStatus): boolean => status === "ACCEPTED" || status === "READY";

export function findByLink(db: Db, orderLinkId: string): OrderRow | undefined {
  return db.prepare("SELECT * FROM orders WHERE order_link_id = ?").get(orderLinkId) as unknown as OrderRow | undefined;
}

export function findById(db: Db, id: string): OrderRow | undefined {
  return db.prepare("SELECT * FROM orders WHERE id = ?").get(id) as unknown as OrderRow | undefined;
}

function itemsOf(db: Db, orderId: string) {
  const rows = db
    .prepare("SELECT menu_item_id, name, quantity, unit_price_minor_lkr FROM order_items WHERE order_id = ? ORDER BY rowid")
    .all(orderId) as unknown as ItemRow[];
  return rows.map((item) => ({
    menuItemId: item.menu_item_id,
    name: item.name,
    quantity: item.quantity,
    unitPriceMinorLkr: item.unit_price_minor_lkr,
  }));
}

function pickupTime(order: OrderRow) {
  return {
    scheduledArrival: order.scheduled_arrival,
    estimatedArrival: order.current_eta,
    etaEvidenceAt: order.eta_evidence_at,
  };
}

// The full order, for SmartRail only: it is the one place the pickup code leaves RailBites.
export function orderView(db: Db, order: OrderRow) {
  const items = itemsOf(db, order.id);
  return {
    id: order.id,
    orderLinkId: order.order_link_id,
    collectionPointId: order.collection_point_id,
    journeyId: order.journey_id,
    pickupStopId: order.pickup_stop_id,
    status: order.status,
    statusSequence: order.status_sequence,
    pickupCode: order.pickup_code,
    items,
    totalMinorLkr: items.reduce((sum, item) => sum + item.quantity * item.unitPriceMinorLkr, 0),
    pickupTime: pickupTime(order),
    cancellationReason: order.cancellation_reason,
    createdAt: order.created_at,
    updatedAt: order.updated_at,
  };
}

// What kiosk staff see: no pickup code and nothing about the passenger.
export function kioskView(db: Db, order: OrderRow) {
  return {
    id: order.id,
    collectionPointId: order.collection_point_id,
    status: order.status,
    statusSequence: order.status_sequence,
    items: itemsOf(db, order.id),
    pickupTime: pickupTime(order),
    cancellationReason: order.cancellation_reason,
  };
}

export interface OrderChange {
  status?: OrderStatus;
  cancellationReason?: CancellationReason;
  eta?: { currentEta: string | null; etaEvidenceAt: string | null; pickupVersion: number };
}

// The only way an order changes after creation. The caller holds the write transaction, so the new
// status, the sequence number, the stock return and the queued callback commit together or not at all.
export function advance(db: Db, order: OrderRow, change: OrderChange, now: Date): OrderRow {
  const status = change.status ?? order.status;
  const at = now.toISOString();

  if (change.status === "CANCELLED") {
    // Reserved stock goes back once, here, because only an open order can reach this branch.
    if (!isOpen(order.status)) throw new Error("Only an open order can be cancelled.");
    db.prepare(
      `UPDATE menu_items SET stock_quantity = stock_quantity +
         (SELECT quantity FROM order_items WHERE order_id = ? AND menu_item_id = menu_items.id)
       WHERE id IN (SELECT menu_item_id FROM order_items WHERE order_id = ?)`,
    ).run(order.id, order.id);
  }

  db.prepare(
    `UPDATE orders SET status = ?, status_sequence = status_sequence + 1, cancellation_reason = ?,
       current_eta = ?, eta_evidence_at = ?, pickup_version = ?, updated_at = ? WHERE id = ?`,
  ).run(
    status,
    change.cancellationReason ?? order.cancellation_reason,
    change.eta ? change.eta.currentEta : order.current_eta,
    change.eta ? change.eta.etaEvidenceAt : order.eta_evidence_at,
    change.eta ? change.eta.pickupVersion : order.pickup_version,
    at,
    order.id,
  );

  const updated = findById(db, order.id)!;
  enqueueUpdate(db, updated, now);
  return updated;
}

// Callback ids are order + sequence, so a retried commit can never produce a second event.
export function enqueueUpdate(db: Db, order: OrderRow, now: Date): void {
  const eventId = `${order.id}-${order.status_sequence}`;
  const payload = {
    eventId,
    orderLinkId: order.order_link_id,
    providerOrderRef: order.id,
    status: order.status,
    statusSequence: order.status_sequence,
    reasonCode: order.cancellation_reason,
    pickupTime: pickupTime(order),
    occurredAt: now.toISOString(),
  };
  db.prepare(
    `INSERT INTO webhook_outbox (event_id, order_id, status_sequence, payload, next_attempt_at, created_at)
     VALUES (?, ?, ?, ?, ?, ?)`,
  ).run(eventId, order.id, order.status_sequence, JSON.stringify(payload), now.toISOString(), now.toISOString());
}
