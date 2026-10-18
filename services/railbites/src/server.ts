import { createServer, type Server } from "node:http";
import { setCutoff } from "./admin";
import { requireAdmin, requireKiosk, requireService } from "./auth";
import { getCollectionPoint, getMenu, listCollectionPoints } from "./catalog";
import type { Config } from "./config";
import { deliverDueCallbacks } from "./callbacks";
import type { Db } from "./db";
import { ALWAYS_REACHABLE, readiness, type DevControls } from "./dev-controls";
import { receiveJourneyEvent } from "./events";
import { ApiError, Router } from "./http";
import { listOrders, markCollected, markNoShow, markReady } from "./kiosk";
import { isUuid, readJson } from "./parse";
import { SessionStore } from "./sessions";
import { RateLimiter, registerStaffRoutes } from "./staff";
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
  /** Present only for local drills; adds the outage switch. */
  dev?: DevControls;
}

export function createApp({ config, db, clock: suppliedClock, passage = httpPassageReader(config), testClock, dev }: AppDependencies): Server {
  const clock = suppliedClock ?? (testClock ? () => testClock.now() : () => new Date());
  const router = new Router();
  const service = (handler: Parameters<typeof requireService>[1]) => requireService(config, handler);
  const admin = (handler: Parameters<typeof requireAdmin>[1]) => requireAdmin(config, handler);
  const kiosk = (handler: Parameters<typeof requireKiosk>[1]) => requireKiosk(config, handler);

  // The staff page keeps its own in-memory sessions and login rate limit. One login is shared by a
  // collection point's staff; no browser ever sees an internal key.
  registerStaffRoutes(router, {
    config,
    db,
    sessions: new SessionStore(),
    loginLimiter: new RateLimiter(config.staffLoginMaxAttempts, config.staffLoginWindowMs),
    clock,
    passage,
  });

  // Health only says the process started; it checks no dependencies.
  router.add("GET", "/health/live", () => ({ status: 200, body: { status: "Healthy" } }));
  router.add("GET", "/health/ready", () => readiness(db, dev));

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

  router.add(
    "PUT",
    "/v1/admin/collection-points/:collectionPointId/cutoff",
    admin(async ({ req, params }) => setCutoff(db, params["collectionPointId"]!, await readJson(req))),
  );

  if (dev) {
    router.add("PUT", "/internal/dev/outage", service(async ({ req }) => {
      const active = ((await readJson(req)) as { active?: unknown } | null)?.active;
      if (typeof active !== "boolean") throw new ApiError(400, "INVALID_REQUEST", "active must be true or false.");
      dev.outage = active;
      return { status: 200, body: { outage: dev.outage } };
    }));
    router.add("GET", "/internal/dev/outage", service(() => ({ status: 200, body: { outage: dev.outage } })));
  }

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
    // The queued callbacks as they stand, so a test can see what is waiting and under which trace.
    router.add("GET", "/internal/test/callbacks", service(() => ({
      status: 200,
      body: {
        callbacks: db
          .prepare("SELECT event_id AS eventId, status_sequence AS statusSequence, attempt_count AS attemptCount, delivered_at AS deliveredAt, last_error AS lastError, traceparent FROM webhook_outbox ORDER BY created_at, rowid")
          .all(),
      },
    })));

    router.add("POST", "/internal/test/callbacks/run", service(async () => ({
      status: 200,
      body: await deliverDueCallbacks(db, config, clock(), fetch, () => dev?.outage ?? false),
    })));
  }

  return createServer((req, res) => {
    // A simulated outage answers like a service that is down, except on the routes that end it.
    if (dev?.outage && !ALWAYS_REACHABLE.includes(new URL(req.url ?? "/", "http://railbites.local").pathname)) {
      res.writeHead(503, { "Content-Type": "application/json", "Retry-After": "5" });
      res.end(JSON.stringify({ code: "SIMULATED_OUTAGE", message: "RailBites is in a simulated outage." }));
      return;
    }
    void router.handle(req, res);
  });
}

// A malformed link ID is a client mistake, not an unknown order.
function linkId(params: Record<string, string>): string {
  const id = params["orderLinkId"]!;
  if (!isUuid(id)) throw new ApiError(400, "INVALID_REQUEST", "orderLinkId must be a UUID.");
  return id.toLowerCase();
}
