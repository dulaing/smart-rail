import { createServer, type Server } from "node:http";
import { requireKiosk, requireService } from "./auth";
import { getCollectionPoint, getMenu, listCollectionPoints } from "./catalog";
import type { Config } from "./config";
import type { Db } from "./db";
import { ApiError, Router } from "./http";
import { listOrders, markCollected, markNoShow, markReady } from "./kiosk";
import { isUuid, readJson } from "./parse";
import { cancelOrder, getOrderByLink, placeOrder } from "./placement";
import { httpPassageReader, type PassageReader } from "./smartrail";

export interface AppDependencies {
  config: Config;
  db: Db;
  /** Time source; tests control it to check cutoffs exactly. */
  clock?: () => Date;
  passage?: PassageReader;
}

export function createApp({ config, db, clock = () => new Date(), passage = httpPassageReader(config) }: AppDependencies): Server {
  const router = new Router();
  const service = (handler: Parameters<typeof requireService>[1]) => requireService(config, handler);
  const kiosk = (handler: Parameters<typeof requireKiosk>[1]) => requireKiosk(config, handler);

  // Health only says the process started; it checks no dependencies.
  router.add("GET", "/health/live", () => ({ status: 200, body: { status: "Healthy" } }));

  router.add("GET", "/v1/collection-points", service(({ url }) => listCollectionPoints(db, url.searchParams.get("smartRailStationId"))));
  router.add("GET", "/v1/collection-points/:collectionPointId", service(({ params }) => getCollectionPoint(db, params["collectionPointId"]!)));
  router.add("GET", "/v1/collection-points/:collectionPointId/menu", service(({ params }) => getMenu(db, params["collectionPointId"]!)));

  router.add("PUT", "/v1/order-links/:orderLinkId", service(async ({ req, params }) => placeOrder(db, clock(), linkId(params), await readJson(req))));
  router.add("GET", "/v1/order-links/:orderLinkId", service(({ params }) => getOrderByLink(db, linkId(params))));
  router.add(
    "POST",
    "/v1/order-links/:orderLinkId/cancellation",
    service(async ({ req, params }) => cancelOrder(db, clock(), linkId(params), await readJson(req))),
  );

  const orders = "/v1/kiosk/collection-points/:collectionPointId/orders";
  router.add("GET", orders, kiosk(({ url, params }) => listOrders(db, params["collectionPointId"]!, url.searchParams.get("status"))));
  router.add("POST", `${orders}/:orderId/ready`, kiosk(({ params }) => markReady(db, clock(), params["collectionPointId"]!, params["orderId"]!)));
  router.add(
    "POST",
    `${orders}/:orderId/collected`,
    kiosk(async ({ req, params }) => markCollected(db, clock(), params["collectionPointId"]!, params["orderId"]!, await readJson(req))),
  );
  router.add(
    "POST",
    `${orders}/:orderId/no-show`,
    kiosk(({ params }) => markNoShow(db, clock, passage, params["collectionPointId"]!, params["orderId"]!)),
  );

  return createServer((req, res) => void router.handle(req, res));
}

// A malformed link ID is a client mistake, not an unknown order.
function linkId(params: Record<string, string>): string {
  const id = params["orderLinkId"]!;
  if (!isUuid(id)) throw new ApiError(400, "INVALID_REQUEST", "orderLinkId must be a UUID.");
  return id.toLowerCase();
}
