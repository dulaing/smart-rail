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
import { hashPassword } from "./staff-auth";
import type { PassageReader, PassageState } from "./smartrail";

export const SERVICE_KEY = "test-service-key";
export const KIOSK_KEYS = new Map([
  ["polgahawela-junction", "kiosk-key-polgahawela"],
  ["peradeniya-junction", "kiosk-key-peradeniya"],
]);

// The shared staff password used by tests. The hash below is what the config carries; the password
// itself never appears in a stored value.
export const STAFF_PASSWORD = "staff-password";
const STAFF_HASH = hashPassword(STAFF_PASSWORD);
export const KIOSK_STAFF = new Map([
  ["polgahawela-junction", STAFF_HASH],
  ["peradeniya-junction", STAFF_HASH],
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
    kioskStaff: KIOSK_STAFF,
    staffSessionLifetimeMs: 3_600_000,
    staffSecureCookies: false,
    staffLoginMaxAttempts: 100,
    staffLoginWindowMs: 300_000,
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

function setCookie(response: Response, name: string): string {
  const raw = response.headers.getSetCookie().find((value) => value.startsWith(`${name}=`));
  if (!raw) throw new Error(`expected a ${name} cookie`);
  return raw.split(";")[0]!;
}

function csrfField(html: string): string {
  const match = /name="csrf" value="([^"]+)"/.exec(html);
  if (!match) throw new Error("expected a CSRF token in the page");
  return match[1]!;
}

// Sign in through the real login form and return the session cookie plus its CSRF token, so a test
// can reach the session-protected staff routes exactly as the browser does.
export async function staffSignIn(
  app: TestApp,
  point = "peradeniya-junction",
  password = STAFF_PASSWORD,
): Promise<{ status: number; cookie: string; csrf: string }> {
  const form = await fetch(`${app.baseUrl}/staff/login`, { redirect: "manual" });
  const loginCookie = setCookie(form, "railbites_kiosk_login");
  const loginCsrf = csrfField(await form.text());
  const response = await fetch(`${app.baseUrl}/staff/login`, {
    method: "POST",
    redirect: "manual",
    headers: { "Content-Type": "application/x-www-form-urlencoded", Cookie: loginCookie },
    body: new URLSearchParams({ collectionPointId: point, password, csrf: loginCsrf }),
  });
  const cookie = setCookie(response, "railbites_kiosk_session");
  const page = await fetch(`${app.baseUrl}/staff`, { headers: { Cookie: cookie } });
  return { status: response.status, cookie, csrf: csrfField(await page.text()) };
}
