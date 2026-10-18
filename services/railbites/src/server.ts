import { createServer, type Server } from "node:http";
import { requireKiosk, requireService } from "./auth";
import { getCollectionPoint, getMenu, listCollectionPoints } from "./catalog";
import type { Config } from "./config";
import { deliverDueCallbacks } from "./callbacks";
import type { Db } from "./db";
import { receiveJourneyEvent } from "./events";
import { ApiError, Router } from "./http";
import { listOrders, markCollected, markNoShow, markReady } from "./kiosk";
import { isUuid, readJson } from "./parse";
import { parseClockRequest, type TestClock } from "./test-clock";
import { cancelOrder, getOrderByLink, placeOrder } from "./placement";
import { httpPassageReader, type PassageReader } from "./smartrail";

export interface AppDependencies {
  config: Config;
  db: Db;
  /** Time source; tests control it to check cutoffs exactly. */
  clock?: () => Date;
  passage?: PassageReader;
  /** Present only in tests and local drills; adds a route to pin the clock. */
  testClock?: TestClock;
}

export function createApp({ config, db, clock: suppliedClock, passage = httpPassageReader(config), testClock }: AppDependencies): Server {
  const clock = suppliedClock ?? (testClock ? () => testClock.now() : () => new Date());
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

  router.add("POST", "/v1/journey-events", service(async ({ req }) => receiveJourneyEvent(db, clock(), await readJson(req))));

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

  if (testClock) {
    router.add("PUT", "/internal/test/clock", service(async ({ req }) => {
      testClock.set(parseClockRequest(await readJson(req)));
      return { status: 200, body: { now: clock().toISOString() } };
    }));
  }

  // Test and local-drill control: run one callback delivery pass now, at the pinned clock.
  if (testClock) {
    router.add("POST", "/internal/test/callbacks/run", service(async () => ({
      status: 200,
      body: await deliverDueCallbacks(db, config, clock()),
    })));
  }

  return createServer((req, res) => void router.handle(req, res));
}

// A malformed link ID is a client mistake, not an unknown order.
function linkId(params: Record<string, string>): string {
  const id = params["orderLinkId"]!;
  if (!isUuid(id)) throw new ApiError(400, "INVALID_REQUEST", "orderLinkId must be a UUID.");
  return id.toLowerCase();
}
