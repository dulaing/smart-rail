export interface Config {
  port: number;
  /** SQLite file. It lives on durable storage; ":memory:" is only for throwaway tests. */
  databasePath: string;
  /** Credential SmartRail sends on service routes. Unset means every service call is refused. */
  serviceKey: string | undefined;
  /** One credential per collection point; a key opens only its own kiosk routes. */
  kioskKeys: ReadonlyMap<string, string>;
  /** Where RailBites reads train passage and (later) posts status callbacks. */
  smartRailBaseUrl: string | undefined;
  /** RailBites' own credential for reading SmartRail; a different secret from `serviceKey`. */
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
    if (at < 1 || !key) throw new Error("RAILBITES_KIOSK_KEYS must look like point-id=key,point-id=key.");
    if ([...keys.values()].includes(key)) throw new Error("Each kiosk needs its own key.");
    keys.set(point, key);
  }
  return keys;
}

// Secrets come from the environment (user secrets / Aspire parameters), never from Git or clients.
export function loadConfig(env: NodeJS.ProcessEnv = process.env): Config {
  return {
    port: Number(env["PORT"] ?? 5200),
    databasePath: env["RAILBITES_DB_PATH"] || "data/railbites.db",
    serviceKey: env["RAILBITES_SERVICE_KEY"] || undefined,
    kioskKeys: parseKioskKeys(env["RAILBITES_KIOSK_KEYS"]),
    smartRailBaseUrl: env["SMARTRAIL_BASE_URL"] || undefined,
    smartRailServiceKey: env["SMARTRAIL_SERVICE_KEY"] || undefined,
    adminKey: env["RAILBITES_ADMIN_KEY"] || undefined,
    callbackSecret: env["RAILBITES_CALLBACK_SECRET"] || undefined,
    callbackPollMs: Number(env["RAILBITES_CALLBACK_POLL_MS"] ?? 2000),
    callbackMaxBackoffSeconds: Number(env["RAILBITES_CALLBACK_MAX_BACKOFF_SECONDS"] ?? 60),
    callbackFailedRetrySeconds: Number(env["RAILBITES_CALLBACK_FAILED_RETRY_SECONDS"] ?? 600),
  };
}
