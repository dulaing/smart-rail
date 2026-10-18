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
];

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
