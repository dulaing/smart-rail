import { createHash } from "node:crypto";
import type { Db } from "./db";
import { immediate } from "./db";
import { ApiError, type Reply } from "./http";
import { applyEligibility, parsePickup, parseSnapshot, type Pickup } from "./journey";
import { advance, isOpen, type OrderRow } from "./orders";
import * as parse from "./parse";
import { eventsReceived } from "./telemetry";

const EVENT_TYPES = ["DELAY", "CANCEL", "EARLY_END", "WITHDRAWAL"];

interface JourneyEvent {
  eventId: string;
  eventType: string;
  journey: ReturnType<typeof parseSnapshot>;
  stops: Pickup[];
}

function parseEvent(value: unknown): JourneyEvent {
  const body = parse.object(value, "body");
  const eventId = parse.text(body["eventId"], "eventId");
  const eventType = parse.text(body["eventType"], "eventType");
  if (!EVENT_TYPES.includes(eventType)) throw parse.invalid("eventType", `must be one of ${EVENT_TYPES.join(", ")}`);
  parse.instant(body["calculatedAt"], "calculatedAt");
  parse.instant(body["occurredAt"], "occurredAt");
  return {
    eventId,
    eventType,
    journey: parseSnapshot(body["journey"], "journey"),
    stops: parse.array(body["stops"], "stops", 200).map((stop, index) => parsePickup(stop, `stops[${index}]`)),
  };
}

// The same message must always hash the same, whatever order its stops arrive in, and a retry carries
// a different timestamp only in headers, never in the body.
function digestOf(event: JourneyEvent): string {
  const stops = [...event.stops].sort((a, b) => (a.stopId < b.stopId ? -1 : 1));
  return createHash("sha256")
    .update(JSON.stringify([event.eventId, event.eventType, event.journey, stops]))
    .digest("hex");
}

// Takes in one confirmed train change. The receipt, the journey's new eligibility, every order it
// cancels or re-times and the callbacks that tell SmartRail all commit together, so a crash leaves
// either all of it or none, and a redelivery after a crash is recognised as a repeat.
export function receiveJourneyEvent(db: Db, now: Date, body: unknown): Reply {
  const event = parseEvent(body);
  const digest = digestOf(event);

  return immediate(db, () => {
    const seen = db.prepare("SELECT content_digest, result FROM received_events WHERE event_id = ?").get(event.eventId) as unknown as
      | { content_digest: string; result: string }
      | undefined;
    if (seen) {
      if (seen.content_digest !== digest) {
        throw new ApiError(409, "EVENT_CONFLICT", "This event id was already received with different content.");
      }
      eventsReceived.add(1, { result: "DUPLICATE" });
      return { status: 200, body: { result: "DUPLICATE" } };
    }

    const applied = db
      .prepare("SELECT applied_journey_version FROM journey_order_states WHERE journey_id = ?")
      .get(event.journey.journeyId) as unknown as { applied_journey_version: number } | undefined;
    const stale = applied !== undefined && event.journey.version < applied.applied_journey_version;

    if (!stale) {
      applyEligibility(db, event.journey, now);
      retimeOpenOrders(db, event, now);
    }

    const result = stale ? "STALE" : "APPLIED";
    db.prepare(
      `INSERT INTO received_events (event_id, journey_id, journey_version, event_type, content_digest, result, received_at)
       VALUES (?, ?, ?, ?, ?, ?, ?)`,
    ).run(event.eventId, event.journey.journeyId, event.journey.version, event.eventType, digest, result, now.toISOString());
    eventsReceived.add(1, { result });
    return { status: 200, body: { result } };
  });
}

// Gives each open order the pickup time from this event, unless the order already holds a time from
// the same or a newer journey version. An unknown (null) time replaces a known one: the provider
// reports what SmartRail confirmed, never a scheduled time in its place. Orders that are no longer
// open are history and are left alone.
function retimeOpenOrders(db: Db, event: JourneyEvent, now: Date): void {
  const byStop = new Map(event.stops.map((stop) => [stop.stopId, stop]));
  const open = db
    .prepare("SELECT * FROM orders WHERE journey_id = ? AND status IN ('ACCEPTED', 'READY') ORDER BY created_at")
    .all(event.journey.journeyId) as unknown as OrderRow[];

  for (const order of open) {
    const stop = byStop.get(order.pickup_stop_id);
    if (!stop || !isOpen(order.status) || order.pickup_version >= event.journey.version) continue;

    const changed = stop.estimatedArrival !== order.current_eta || stop.etaEvidenceAt !== order.eta_evidence_at;
    if (changed) {
      advance(
        db,
        order,
        { eta: { currentEta: stop.estimatedArrival, etaEvidenceAt: stop.etaEvidenceAt, pickupVersion: event.journey.version } },
        now,
      );
    } else {
      db.prepare("UPDATE orders SET pickup_version = ? WHERE id = ?").run(event.journey.version, order.id);
    }
  }
}
