import type { Db } from "./db";
import { ApiError, type Reply } from "./http";

interface CollectionPointRow {
  id: string;
  smart_rail_station_id: string;
  name: string;
  cutoff_minutes: number;
}

interface MenuItemRow {
  id: string;
  name: string;
  price_minor_lkr: number;
  stock_quantity: number;
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function toCollectionPoint(row: CollectionPointRow) {
  return {
    id: row.id,
    smartRailStationId: row.smart_rail_station_id,
    name: row.name,
    cutoffMinutes: row.cutoff_minutes,
  };
}

// The cutoff belongs to Grilled and changes without a SmartRail release, so callers must not
// cache these responses. The menu below may be cached for a minute; stock is rechecked at submit.
const NO_STORE = { "Cache-Control": "no-store" };
const MENU_MAX_AGE_SECONDS = 60;

export function listCollectionPoints(db: Db, stationId: string | null): Reply {
  if (stationId !== null && !UUID.test(stationId)) {
    throw new ApiError(400, "INVALID_REQUEST", "smartRailStationId must be a UUID.");
  }
  const rows = (
    stationId === null
      ? db.prepare("SELECT * FROM collection_points WHERE active = 1 ORDER BY name").all()
      : db
          .prepare("SELECT * FROM collection_points WHERE active = 1 AND smart_rail_station_id = ? ORDER BY name")
          .all(stationId.toLowerCase())
  ) as unknown as CollectionPointRow[];
  return { status: 200, body: { collectionPoints: rows.map(toCollectionPoint) }, headers: NO_STORE };
}

function findCollectionPoint(db: Db, id: string): CollectionPointRow {
  const row = db.prepare("SELECT * FROM collection_points WHERE id = ? AND active = 1").get(id) as
    | CollectionPointRow
    | undefined;
  if (!row) throw new ApiError(404, "COLLECTION_POINT_NOT_FOUND", "Unknown collection point.");
  return row;
}

export function getCollectionPoint(db: Db, id: string): Reply {
  return { status: 200, body: toCollectionPoint(findCollectionPoint(db, id)), headers: NO_STORE };
}

// A display name for the staff page heading. Falls back to the id so a page never shows blank.
export function collectionPointName(db: Db, id: string): string {
  const row = db.prepare("SELECT name FROM collection_points WHERE id = ?").get(id) as { name: string } | undefined;
  return row?.name ?? id;
}

export function getMenu(db: Db, collectionPointId: string): Reply {
  const point = findCollectionPoint(db, collectionPointId);
  const items = db
    .prepare(
      `SELECT id, name, price_minor_lkr, stock_quantity FROM menu_items
       WHERE collection_point_id = ? AND active = 1 ORDER BY sort_order, id`,
    )
    .all(point.id) as unknown as MenuItemRow[];
  return {
    status: 200,
    body: {
      collectionPointId: point.id,
      currency: "LKR",
      items: items.map((item) => ({
        id: item.id,
        name: item.name,
        priceMinorLkr: item.price_minor_lkr,
        stockQuantity: item.stock_quantity,
      })),
    },
    headers: { "Cache-Control": `private, max-age=${MENU_MAX_AGE_SECONDS}` },
  };
}
