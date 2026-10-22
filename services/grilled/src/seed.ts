import { immediate, type Db } from "./db";

// SmartRail station IDs are deterministic: MD5("smartrail:station:<code>") read as a .NET Guid
// (StableIds.For in services/api). They are copied here, not computed, so the two services share
// identifiers and no code. The codes are PLG and PDN; the Rail seed adds these stations with them.
export const POLGAHAWELA_STATION_ID = "ea24d5bf-95a2-db6c-e85c-ff4705645a2f";
export const PERADENIYA_STATION_ID = "04111fa9-fc9c-3657-e13b-d606dbe37c55";

interface MenuSeed {
  id: string;
  name: string;
  priceMinorLkr: number;
  stock: number;
}

interface CollectionPointSeed {
  id: string;
  smartRailStationId: string;
  name: string;
  menu: MenuSeed[];
}

const DEFAULT_CUTOFF_MINUTES = 30;

// Prices are minor LKR (cents): 65000 is Rs 650.00.
const collectionPoints: CollectionPointSeed[] = [
  {
    id: "polgahawela-junction",
    smartRailStationId: POLGAHAWELA_STATION_ID,
    name: "Polgahawela Junction",
    menu: [
      { id: "plg-chicken-rice-curry", name: "Chicken rice and curry", priceMinorLkr: 65000, stock: 40 },
      { id: "plg-vegetable-rice-curry", name: "Vegetable rice and curry", priceMinorLkr: 50000, stock: 40 },
      { id: "plg-egg-roti", name: "Egg roti", priceMinorLkr: 15000, stock: 60 },
      { id: "plg-vegetable-roll", name: "Vegetable roll", priceMinorLkr: 12000, stock: 60 },
      { id: "plg-plain-tea", name: "Plain tea", priceMinorLkr: 8000, stock: 80 },
    ],
  },
  {
    id: "peradeniya-junction",
    smartRailStationId: PERADENIYA_STATION_ID,
    name: "Peradeniya Junction",
    menu: [
      { id: "pdn-chicken-rice-curry", name: "Chicken rice and curry", priceMinorLkr: 70000, stock: 40 },
      { id: "pdn-lamprais", name: "Lamprais", priceMinorLkr: 85000, stock: 25 },
      { id: "pdn-fish-bun", name: "Fish bun", priceMinorLkr: 10000, stock: 60 },
      { id: "pdn-string-hoppers", name: "String hoppers with sambol", priceMinorLkr: 45000, stock: 30 },
      { id: "pdn-plain-tea", name: "Plain tea", priceMinorLkr: 8000, stock: 80 },
    ],
  },
];

// Repeatable on every start. A new row gets its starting stock and the default cutoff; an existing
// row keeps its stock, cutoff and active flag, because those change while the service runs and a
// restart must not reset them. Only the catalogue facts (name, price, station mapping) follow the seed.
export function seedCatalog(db: Db): void {
  const upsertPoint = db.prepare(`
    INSERT INTO collection_points (id, smart_rail_station_id, name, cutoff_minutes)
    VALUES (?, ?, ?, ?)
    ON CONFLICT (id) DO UPDATE SET
      smart_rail_station_id = excluded.smart_rail_station_id,
      name = excluded.name
  `);
  const upsertItem = db.prepare(`
    INSERT INTO menu_items (id, collection_point_id, name, price_minor_lkr, stock_quantity, sort_order)
    VALUES (?, ?, ?, ?, ?, ?)
    ON CONFLICT (id) DO UPDATE SET
      collection_point_id = excluded.collection_point_id,
      name = excluded.name,
      price_minor_lkr = excluded.price_minor_lkr,
      sort_order = excluded.sort_order
  `);

  immediate(db, () => {
    for (const point of collectionPoints) {
      upsertPoint.run(point.id, point.smartRailStationId, point.name, DEFAULT_CUTOFF_MINUTES);
      point.menu.forEach((item, index) => {
        upsertItem.run(item.id, point.id, item.name, item.priceMinorLkr, item.stock, index);
      });
    }
  });
}
