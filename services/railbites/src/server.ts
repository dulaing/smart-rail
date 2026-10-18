import { createServer, type Server } from "node:http";
import { requireService } from "./auth";
import { getCollectionPoint, getMenu, listCollectionPoints } from "./catalog";
import type { Config } from "./config";
import type { Db } from "./db";
import { Router } from "./http";

export interface AppDependencies {
  config: Config;
  db: Db;
}

export function createApp({ config, db }: AppDependencies): Server {
  const router = new Router();

  // Health only says the process started; it checks no dependencies.
  router.add("GET", "/health/live", () => ({ status: 200, body: { status: "Healthy" } }));

  router.add(
    "GET",
    "/v1/collection-points",
    requireService(config, ({ url }) => listCollectionPoints(db, url.searchParams.get("smartRailStationId"))),
  );
  router.add(
    "GET",
    "/v1/collection-points/:collectionPointId",
    requireService(config, ({ params }) => getCollectionPoint(db, params["collectionPointId"]!)),
  );
  router.add(
    "GET",
    "/v1/collection-points/:collectionPointId/menu",
    requireService(config, ({ params }) => getMenu(db, params["collectionPointId"]!)),
  );

  return createServer((req, res) => void router.handle(req, res));
}
