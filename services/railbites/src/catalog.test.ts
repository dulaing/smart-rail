import assert from "node:assert/strict";
import { after, before, describe, test } from "node:test";
import { migrate } from "./migrations";
import { PERADENIYA_STATION_ID, POLGAHAWELA_STATION_ID, seedCatalog } from "./seed";
import { SERVICE_KEY, serviceHeaders, startApp, temporaryDatabasePath, type TestApp } from "./test-support";

const database = temporaryDatabasePath();
let app: TestApp;

before(async () => {
  app = await startApp({ databasePath: database.path });
});

after(async () => {
  await app.close();
  database.remove();
});

const get = (path: string, headers: Record<string, string> = serviceHeaders()) =>
  fetch(`${app.baseUrl}${path}`, { headers });

interface MenuBody {
  collectionPointId: string;
  currency: string;
  items: { id: string; name: string; priceMinorLkr: number; stockQuantity: number }[];
}

describe("service credential", () => {
  const routes = [
    "/v1/collection-points",
    "/v1/collection-points/polgahawela-junction",
    "/v1/collection-points/polgahawela-junction/menu",
  ];

  for (const route of routes) {
    test(`${route} refuses a missing or wrong credential`, async () => {
      assert.equal((await get(route, {})).status, 401);
      assert.equal((await get(route, serviceHeaders("wrong-key"))).status, 401);
      // A kiosk-style or bearer header is not the service credential.
      assert.equal((await get(route, { Authorization: `Bearer ${SERVICE_KEY}` })).status, 401);
    });
  }

  test("fails closed when no key is configured, even if the caller sends one", async () => {
    const path = temporaryDatabasePath();
    const unconfigured = await startApp({ databasePath: path.path, serviceKey: undefined });
    try {
      for (const headers of [{}, serviceHeaders(""), serviceHeaders("undefined")]) {
        const res = await fetch(`${unconfigured.baseUrl}/v1/collection-points`, { headers });
        assert.equal(res.status, 401);
      }
    } finally {
      await unconfigured.close();
      path.remove();
    }
  });

  test("a refusal does not leak whether the collection point exists", async () => {
    const known = await get("/v1/collection-points/polgahawela-junction/menu", {});
    const unknown = await get("/v1/collection-points/nowhere/menu", {});
    assert.equal(known.status, 401);
    assert.equal(unknown.status, 401);
  });
});

describe("collection points", () => {
  test("lists both pilot stations mapped to their SmartRail station IDs, with the provider cutoff", async () => {
    const res = await get("/v1/collection-points");
    assert.equal(res.status, 200);
    assert.equal(res.headers.get("cache-control"), "no-store");
    assert.deepEqual(await res.json(), {
      collectionPoints: [
        { id: "peradeniya-junction", smartRailStationId: PERADENIYA_STATION_ID, name: "Peradeniya Junction", cutoffMinutes: 30 },
        { id: "polgahawela-junction", smartRailStationId: POLGAHAWELA_STATION_ID, name: "Polgahawela Junction", cutoffMinutes: 30 },
      ],
    });
  });

  test("filters by SmartRail station and returns nothing for an unmapped station", async () => {
    const mapped = (await (await get(`/v1/collection-points?smartRailStationId=${PERADENIYA_STATION_ID.toUpperCase()}`)).json()) as {
      collectionPoints: { id: string }[];
    };
    assert.deepEqual(mapped.collectionPoints.map((p) => p.id), ["peradeniya-junction"]);

    const other = (await (await get("/v1/collection-points?smartRailStationId=00000000-0000-0000-0000-000000000001")).json()) as {
      collectionPoints: unknown[];
    };
    assert.deepEqual(other.collectionPoints, []);
  });

  test("rejects a malformed station ID", async () => {
    const res = await get("/v1/collection-points?smartRailStationId=peradeniya");
    assert.equal(res.status, 400);
    assert.equal(((await res.json()) as { code: string }).code, "INVALID_REQUEST");
  });

  test("an unknown collection point is 404", async () => {
    for (const path of ["/v1/collection-points/nowhere", "/v1/collection-points/nowhere/menu"]) {
      const res = await get(path);
      assert.equal(res.status, 404);
      assert.equal(((await res.json()) as { code: string }).code, "COLLECTION_POINT_NOT_FOUND");
    }
  });

  test("a changed cutoff is visible at once and never cached", async () => {
    app.db.prepare("UPDATE collection_points SET cutoff_minutes = 45 WHERE id = 'peradeniya-junction'").run();
    const res = await get("/v1/collection-points/peradeniya-junction");
    assert.equal(res.headers.get("cache-control"), "no-store");
    assert.equal(((await res.json()) as { cutoffMinutes: number }).cutoffMinutes, 45);
    app.db.prepare("UPDATE collection_points SET cutoff_minutes = 30 WHERE id = 'peradeniya-junction'").run();
  });
});

describe("menu", () => {
  test("returns active items with integer minor-LKR prices and stock, cacheable for 60 seconds", async () => {
    const res = await get("/v1/collection-points/peradeniya-junction/menu");
    assert.equal(res.status, 200);
    assert.equal(res.headers.get("cache-control"), "private, max-age=60");
    const menu = (await res.json()) as MenuBody;
    assert.equal(menu.collectionPointId, "peradeniya-junction");
    assert.equal(menu.currency, "LKR");
    assert.ok(menu.items.length >= 3);
    for (const item of menu.items) {
      assert.ok(item.id.startsWith("pdn-"), `${item.id} belongs to Peradeniya`);
      assert.ok(Number.isInteger(item.priceMinorLkr) && item.priceMinorLkr > 0);
      assert.ok(Number.isInteger(item.stockQuantity) && item.stockQuantity >= 0);
    }
    const lamprais = menu.items.find((item) => item.id === "pdn-lamprais");
    assert.deepEqual(lamprais, { id: "pdn-lamprais", name: "Lamprais", priceMinorLkr: 85000, stockQuantity: 25 });
  });

  test("each collection point shows only its own menu", async () => {
    const menu = (await (await get("/v1/collection-points/polgahawela-junction/menu")).json()) as MenuBody;
    assert.ok(menu.items.every((item) => item.id.startsWith("plg-")));
  });

  test("hides inactive items but shows sold-out ones with zero stock", async () => {
    app.db.prepare("UPDATE menu_items SET active = 0 WHERE id = 'plg-plain-tea'").run();
    app.db.prepare("UPDATE menu_items SET stock_quantity = 0 WHERE id = 'plg-egg-roti'").run();
    try {
      const menu = (await (await get("/v1/collection-points/polgahawela-junction/menu")).json()) as MenuBody;
      assert.equal(menu.items.find((item) => item.id === "plg-plain-tea"), undefined);
      assert.equal(menu.items.find((item) => item.id === "plg-egg-roti")?.stockQuantity, 0);
    } finally {
      app.db.prepare("UPDATE menu_items SET active = 1 WHERE id = 'plg-plain-tea'").run();
      app.db.prepare("UPDATE menu_items SET stock_quantity = 60 WHERE id = 'plg-egg-roti'").run();
    }
  });
});

describe("setup and reseed", () => {
  const count = (sql: string) => (app.db.prepare(sql).get() as { n: number }).n;

  test("migrating and seeding again changes nothing", () => {
    const before = [count("SELECT count(*) n FROM collection_points"), count("SELECT count(*) n FROM menu_items")];
    migrate(app.db);
    seedCatalog(app.db);
    seedCatalog(app.db);
    assert.deepEqual(
      [count("SELECT count(*) n FROM collection_points"), count("SELECT count(*) n FROM menu_items")],
      before,
    );
    assert.equal(count("SELECT count(*) n FROM schema_migrations"), 2);
  });

  test("a restart-style reseed keeps live stock, cutoff and availability but restores catalogue facts", () => {
    app.db.prepare("UPDATE menu_items SET stock_quantity = 3, active = 0, price_minor_lkr = 1 WHERE id = 'pdn-fish-bun'").run();
    app.db.prepare("UPDATE collection_points SET cutoff_minutes = 45 WHERE id = 'polgahawela-junction'").run();
    try {
      seedCatalog(app.db);
      const item = app.db.prepare("SELECT stock_quantity, active, price_minor_lkr FROM menu_items WHERE id = 'pdn-fish-bun'").get();
      assert.deepEqual({ ...item }, { stock_quantity: 3, active: 0, price_minor_lkr: 10000 });
      const point = app.db.prepare("SELECT cutoff_minutes FROM collection_points WHERE id = 'polgahawela-junction'").get();
      assert.equal(point?.["cutoff_minutes"], 45);
    } finally {
      app.db.prepare("UPDATE menu_items SET stock_quantity = 60, active = 1 WHERE id = 'pdn-fish-bun'").run();
      app.db.prepare("UPDATE collection_points SET cutoff_minutes = 30 WHERE id = 'polgahawela-junction'").run();
    }
  });
});

test("data survives stopping and starting the process on the same file", async () => {
  const path = temporaryDatabasePath();
  try {
    const first = await startApp({ databasePath: path.path });
    first.db.prepare("UPDATE menu_items SET stock_quantity = 7 WHERE id = 'pdn-lamprais'").run();
    first.db.prepare("UPDATE collection_points SET cutoff_minutes = 45 WHERE id = 'peradeniya-junction'").run();
    await first.close();

    const second = await startApp({ databasePath: path.path });
    try {
      const menu = (await (await fetch(`${second.baseUrl}/v1/collection-points/peradeniya-junction/menu`, { headers: serviceHeaders() })).json()) as MenuBody;
      assert.equal(menu.items.find((item) => item.id === "pdn-lamprais")?.stockQuantity, 7);
      const point = (await (await fetch(`${second.baseUrl}/v1/collection-points/peradeniya-junction`, { headers: serviceHeaders() })).json()) as { cutoffMinutes: number };
      assert.equal(point.cutoffMinutes, 45);
    } finally {
      await second.close();
    }
  } finally {
    path.remove();
  }
});
