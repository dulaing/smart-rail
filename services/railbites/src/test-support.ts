import { mkdtempSync, rmSync } from "node:fs";
import type { AddressInfo } from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";
import type { Config } from "./config";
import { openDatabase, type Db } from "./db";
import { migrate } from "./migrations";
import { seedCatalog } from "./seed";
import type { DevControls } from "./dev-controls";
import { createApp } from "./server";
import type { PassageReader, PassageState } from "./smartrail";

export const SERVICE_KEY = "test-service-key";
export const KIOSK_KEYS = new Map([
  ["polgahawela-junction", "kiosk-key-polgahawela"],
  ["peradeniya-junction", "kiosk-key-peradeniya"],
]);

export interface TestApp {
  config: Config;
  baseUrl: string;
  db: Db;
  databasePath: string;
  /** Move the app's clock; every request reads it. */
  setNow(instant: string): void;
  /** What SmartRail would answer about the train passing a stop. */
  setPassage(state: PassageState | "ERROR"): void;
  close(): Promise<void>;
}

export const START = "2026-10-18T05:00:00.000Z";

export function temporaryDatabasePath(): { path: string; remove(): void } {
  const dir = mkdtempSync(join(tmpdir(), "railbites-"));
  return { path: join(dir, "railbites.db"), remove: () => rmSync(dir, { recursive: true, force: true }) };
}

// Starts the real app on a random port over a file-backed SQLite database, like production does.
export async function startApp(options: {
  databasePath: string;
  serviceKey?: string | undefined;
  now?: string;
  overrides?: Partial<Config>;
  dev?: DevControls;
}): Promise<TestApp> {
  const config: Config = {
    port: 0,
    databasePath: options.databasePath,
    serviceKey: "serviceKey" in options ? options.serviceKey : SERVICE_KEY,
    kioskKeys: KIOSK_KEYS,
    smartRailBaseUrl: undefined,
    smartRailServiceKey: undefined,
    adminKey: undefined,
    callbackSecret: undefined,
    callbackPollMs: 0,
    callbackMaxBackoffSeconds: 60,
    callbackFailedRetrySeconds: 600,
    ...options.overrides,
  };
  const db = openDatabase(config.databasePath);
  migrate(db);
  seedCatalog(db);

  let now = options.now ?? START;
  let passage: PassageState | "ERROR" = "UNKNOWN";
  const passageReader: PassageReader = {
    read: async () => {
      if (passage === "ERROR") throw new Error("SmartRail is down.");
      return passage;
    },
  };

  const server = createApp({ config, db, clock: () => new Date(now), passage: passageReader, ...(options.dev ? { dev: options.dev } : {}) });
  await new Promise<void>((resolve) => server.listen(0, resolve));
  return {
    config,
    baseUrl: `http://localhost:${(server.address() as AddressInfo).port}`,
    db,
    databasePath: config.databasePath,
    setNow: (instant) => {
      now = instant;
    },
    setPassage: (state) => {
      passage = state;
    },
    close: async () => {
      await new Promise<void>((resolve) => {
        server.close(() => resolve());
        server.closeAllConnections();
      });
      db.close();
    },
  };
}

export function serviceHeaders(key: string = SERVICE_KEY): Record<string, string> {
  return { "X-RailBites-Service-Key": key };
}

export function kioskHeaders(collectionPointId: string): Record<string, string> {
  return { "X-RailBites-Kiosk-Key": KIOSK_KEYS.get(collectionPointId)! };
}
