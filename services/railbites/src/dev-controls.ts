import type { Db } from "./db";
import type { Reply } from "./http";
import { LATEST_MIGRATION } from "./migrations";

// Local-drill controls. They exist only when RAILBITES_DEV_CONTROLS=1 outside production, and every
// call needs the service credential. The outage switch makes the provider behave like a stopped
// service from outside (every /v1 call answers 503, queued callbacks wait) while the restore route and
// the health routes stay reachable, so the drill can be ended without restarting anything.
export interface DevControls {
  outage: boolean;
}

// The test controls exist only in tests, where the clock must stay settable through a simulated outage.
export const ALWAYS_REACHABLE = ["/health/live", "/health/ready", "/internal/dev/outage", "/internal/test/clock", "/internal/test/callbacks/run", "/internal/test/callbacks"];

// Readiness is what a load balancer or the AppHost asks: can this process do its job?
export function readiness(db: Db, dev: DevControls | undefined): Reply {
  if (dev?.outage) {
    return { status: 503, body: { status: "Unhealthy", checks: { simulatedOutage: "Active" } } };
  }
  try {
    db.prepare("SELECT 1").get();
    const applied = (db.prepare("SELECT max(id) AS id FROM schema_migrations").get() as { id: number | null }).id;
    const upToDate = applied === LATEST_MIGRATION;
    return {
      status: upToDate ? 200 : 503,
      body: { status: upToDate ? "Healthy" : "Unhealthy", checks: { database: "Healthy", migrations: upToDate ? "Healthy" : "Behind" } },
    };
  } catch {
    return { status: 503, body: { status: "Unhealthy", checks: { database: "Unhealthy" } } };
  }
}
