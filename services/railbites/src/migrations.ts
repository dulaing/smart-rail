import { immediate, type Db } from "./db";

interface Migration {
  id: number;
  name: string;
  sql: string;
}

// Append only. A released migration is never edited; later tasks add the next id.
// Instants are stored as UTC ISO-8601 text (2026-10-18T09:30:00.000Z); SmartRail IDs as lower-case UUID text.
const migrations: Migration[] = [
  {
    id: 1,
    name: "collection points and menu",
    sql: `
      CREATE TABLE collection_points (
        id TEXT PRIMARY KEY,
        smart_rail_station_id TEXT NOT NULL UNIQUE,
        name TEXT NOT NULL,
        cutoff_minutes INTEGER NOT NULL DEFAULT 30 CHECK (cutoff_minutes BETWEEN 0 AND 240),
        active INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1))
      ) STRICT;

      CREATE TABLE menu_items (
        id TEXT PRIMARY KEY,
        collection_point_id TEXT NOT NULL REFERENCES collection_points (id),
        name TEXT NOT NULL,
        price_minor_lkr INTEGER NOT NULL CHECK (price_minor_lkr >= 0),
        stock_quantity INTEGER NOT NULL CHECK (stock_quantity >= 0),
        active INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1)),
        sort_order INTEGER NOT NULL DEFAULT 0
      ) STRICT;

      CREATE INDEX menu_items_by_collection_point ON menu_items (collection_point_id, sort_order);
    `,
  },
  {
    id: 2,
    name: "orders, journey eligibility and callback outbox",
    sql: `
      -- One row per journey, created by the first order attempt or train-change event.
      -- blocked_pickup_stop_ids is a JSON array of lower-case UUIDs.
      CREATE TABLE journey_order_states (
        journey_id TEXT PRIMARY KEY,
        applied_journey_version INTEGER NOT NULL CHECK (applied_journey_version >= 0),
        order_closed INTEGER NOT NULL CHECK (order_closed IN (0, 1)),
        blocked_pickup_stop_ids TEXT NOT NULL CHECK (json_valid(blocked_pickup_stop_ids)),
        updated_at TEXT NOT NULL
      ) STRICT;

      CREATE TABLE orders (
        id TEXT PRIMARY KEY,
        order_link_id TEXT NOT NULL UNIQUE,
        request_digest TEXT NOT NULL,
        journey_id TEXT NOT NULL REFERENCES journey_order_states (journey_id),
        train_number TEXT NOT NULL,
        service_date TEXT NOT NULL,
        pickup_stop_id TEXT NOT NULL,
        collection_point_id TEXT NOT NULL REFERENCES collection_points (id),
        pickup_code TEXT NOT NULL UNIQUE,
        status TEXT NOT NULL CHECK (status IN ('ACCEPTED', 'READY', 'COLLECTED', 'NO_SHOW', 'CANCELLED')),
        status_sequence INTEGER NOT NULL CHECK (status_sequence >= 1),
        cancellation_reason TEXT CHECK (cancellation_reason IN ('PASSENGER_CANCELLED', 'TRAIN_CANCELLED', 'TRAIN_ENDED_EARLY')),
        scheduled_arrival TEXT NOT NULL,
        current_eta TEXT,
        eta_evidence_at TEXT,
        pickup_version INTEGER NOT NULL,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL
      ) STRICT;

      CREATE INDEX orders_by_collection_point ON orders (collection_point_id, status);
      CREATE INDEX orders_by_journey ON orders (journey_id, status);

      CREATE TABLE order_items (
        id TEXT PRIMARY KEY,
        order_id TEXT NOT NULL REFERENCES orders (id),
        menu_item_id TEXT NOT NULL REFERENCES menu_items (id),
        name TEXT NOT NULL,
        quantity INTEGER NOT NULL CHECK (quantity > 0),
        unit_price_minor_lkr INTEGER NOT NULL CHECK (unit_price_minor_lkr >= 0)
      ) STRICT;

      CREATE INDEX order_items_by_order ON order_items (order_id);

      -- Written in the same transaction as the status change it describes. payload is the exact
      -- JSON body to send, so every retry carries the same bytes.
      CREATE TABLE webhook_outbox (
        event_id TEXT PRIMARY KEY,
        order_id TEXT NOT NULL REFERENCES orders (id),
        status_sequence INTEGER NOT NULL,
        payload TEXT NOT NULL CHECK (json_valid(payload)),
        attempt_count INTEGER NOT NULL DEFAULT 0,
        next_attempt_at TEXT NOT NULL,
        delivered_at TEXT,
        last_error TEXT,
        created_at TEXT NOT NULL,
        UNIQUE (order_id, status_sequence)
      ) STRICT;

      CREATE INDEX webhook_outbox_pending ON webhook_outbox (next_attempt_at) WHERE delivered_at IS NULL;
    `,
  },
  {
    id: 3,
    name: "received train-change events",
    sql: `
      -- One row per train-change event ever received, written in the same transaction as its effects.
      -- content_digest lets a repeat of the same event id be told from a different message under it.
      CREATE TABLE received_events (
        event_id TEXT PRIMARY KEY,
        journey_id TEXT NOT NULL,
        journey_version INTEGER NOT NULL,
        event_type TEXT NOT NULL,
        content_digest TEXT NOT NULL,
        result TEXT NOT NULL CHECK (result IN ('APPLIED', 'STALE')),
        received_at TEXT NOT NULL
      ) STRICT;

      CREATE INDEX received_events_by_journey ON received_events (journey_id, journey_version);
    `,
  },
  {
    id: 4,
    name: "trace identity on queued callbacks",
    sql: `
      -- The W3C traceparent of the request that caused a status change, so the callback that tells
      -- SmartRail about it continues the same trace. Null for rows queued before this existed.
      ALTER TABLE webhook_outbox ADD COLUMN traceparent TEXT;
    `,
  },
];

export const LATEST_MIGRATION = migrations[migrations.length - 1]!.id;

// Explicit setup, run once at start-up. Safe to run again: applied migrations are skipped.
export function migrate(db: Db): void {
  db.exec(`
    CREATE TABLE IF NOT EXISTS schema_migrations (
      id INTEGER PRIMARY KEY,
      name TEXT NOT NULL,
      applied_at TEXT NOT NULL
    ) STRICT
  `);

  const applied = db.prepare("SELECT id FROM schema_migrations ORDER BY id").all() as { id: number }[];
  const newest = migrations[migrations.length - 1]!.id;
  const unknown = applied.find((row) => row.id > newest);
  if (unknown) {
    throw new Error(`Database has migration ${unknown.id}, newer than this build (${newest}).`);
  }

  const done = new Set(applied.map((row) => row.id));
  for (const migration of migrations) {
    if (done.has(migration.id)) continue;
    immediate(db, () => {
      db.exec(migration.sql);
      db.prepare("INSERT INTO schema_migrations (id, name, applied_at) VALUES (?, ?, ?)").run(
        migration.id,
        migration.name,
        new Date().toISOString(),
      );
    });
  }
}
