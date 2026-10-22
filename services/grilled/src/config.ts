export interface Config {
  port: number;
  /** SQLite file. It lives on durable storage; ":memory:" is only for throwaway tests. */
  databasePath: string;
  /** Credential SmartRail sends on service routes. Unset means every service call is refused. */
  serviceKey: string | undefined;
  /** One credential per collection point; a key opens only its own kiosk routes. */
  kioskKeys: ReadonlyMap<string, string>;
  /**
   * One shared human staff login per collection point, as point-id -> salted password hash. This is a
   * different secret from `kioskKeys`: the browser types the password, and only the hash is stored.
   * Empty means the staff login cannot be used at all.
   */
  kioskStaff: ReadonlyMap<string, string>;
  /** How long a staff session lasts before it must be renewed by logging in again. */
  staffSessionLifetimeMs: number;
  /** Send the session cookie Secure. False is allowed only for explicit local HTTP development. */
  staffSecureCookies: boolean;
  /** Most staff login attempts allowed from one address inside `staffLoginWindowMs`. */
  staffLoginMaxAttempts: number;
  /** Length of the staff login rate-limit window. */
  staffLoginWindowMs: number;
  /** Where Grilled reads train passage and (later) posts status callbacks. */
  smartRailBaseUrl: string | undefined;
  /** Grilled' own credential for reading SmartRail; a different secret from `serviceKey`. */
  smartRailServiceKey: string | undefined;
  /** Credential for the provider's own policy changes, such as the ordering cutoff. Unset refuses them all. */
  adminKey: string | undefined;
  /** Shared secret that signs status callbacks. Unset means no callback is sent. */
  callbackSecret: string | undefined;
  /** How often the callback loop looks for queued callbacks; 0 turns the loop off. */
  callbackPollMs: number;
  /** Longest wait between retries after a timeout or 5xx. */
  callbackMaxBackoffSeconds: number;
  /** Wait before retrying a callback SmartRail refused for good (for example a wrong secret). */
  callbackFailedRetrySeconds: number;
}

// "polgahawela-junction=key1,peradeniya-junction=key2"
function parseKioskKeys(value: string | undefined): Map<string, string> {
  const keys = new Map<string, string>();
  for (const pair of (value ?? "").split(",").map((p) => p.trim()).filter(Boolean)) {
    const at = pair.indexOf("=");
    const point = pair.slice(0, at).trim();
    const key = pair.slice(at + 1).trim();
    if (at < 1 || !key) throw new Error("GRILLED_KIOSK_KEYS must look like point-id=key,point-id=key.");
    if ([...keys.values()].includes(key)) throw new Error("Each kiosk needs its own key.");
    keys.set(point, key);
  }
  return keys;
}

// "polgahawela-junction=scrypt$...,peradeniya-junction=scrypt$...". The hash never contains `=`
// (base64url), so splitting on the first `=` keeps every part of the hash intact.
function parseKioskStaff(value: string | undefined): Map<string, string> {
  const staff = new Map<string, string>();
  for (const pair of (value ?? "").split(",").map((p) => p.trim()).filter(Boolean)) {
    const at = pair.indexOf("=");
    const point = pair.slice(0, at).trim();
    const hash = pair.slice(at + 1).trim();
    if (at < 1 || !hash) throw new Error("GRILLED_KIOSK_STAFF must look like point-id=password-hash,point-id=password-hash.");
    staff.set(point, hash);
  }
  return staff;
}

// Secrets come from the environment (user secrets / Aspire parameters), never from Git or clients.
export function loadConfig(env: NodeJS.ProcessEnv = process.env): Config {
  return {
    port: Number(env["PORT"] ?? 5200),
    databasePath: env["GRILLED_DB_PATH"] || "data/grilled.db",
    serviceKey: env["GRILLED_SERVICE_KEY"] || undefined,
    kioskKeys: parseKioskKeys(env["GRILLED_KIOSK_KEYS"]),
    kioskStaff: parseKioskStaff(env["GRILLED_KIOSK_STAFF"]),
    staffSessionLifetimeMs: Number(env["GRILLED_KIOSK_SESSION_MINUTES"] ?? 60) * 60_000,
    // Secure by default. The only way to drop it is an explicit local-development flag outside production.
    staffSecureCookies: !(env["GRILLED_KIOSK_INSECURE_COOKIES"] === "1" && env["NODE_ENV"] !== "production"),
    staffLoginMaxAttempts: Number(env["GRILLED_KIOSK_LOGIN_MAX_ATTEMPTS"] ?? 10),
    staffLoginWindowMs: Number(env["GRILLED_KIOSK_LOGIN_WINDOW_MS"] ?? 300_000),
    smartRailBaseUrl: env["SMARTRAIL_BASE_URL"] || undefined,
    smartRailServiceKey: env["SMARTRAIL_SERVICE_KEY"] || undefined,
    adminKey: env["GRILLED_ADMIN_KEY"] || undefined,
    callbackSecret: env["GRILLED_CALLBACK_SECRET"] || undefined,
    callbackPollMs: Number(env["GRILLED_CALLBACK_POLL_MS"] ?? 2000),
    callbackMaxBackoffSeconds: Number(env["GRILLED_CALLBACK_MAX_BACKOFF_SECONDS"] ?? 60),
    callbackFailedRetrySeconds: Number(env["GRILLED_CALLBACK_FAILED_RETRY_SECONDS"] ?? 600),
  };
}
