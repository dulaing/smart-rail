export interface Config {
  port: number;
  /** SQLite file. It lives on durable storage; ":memory:" is only for throwaway tests. */
  databasePath: string;
  /** Credential SmartRail sends on service routes. Unset means every service call is refused. */
  serviceKey: string | undefined;
}

// Secrets come from the environment (user secrets / Aspire parameters), never from Git or clients.
export function loadConfig(env: NodeJS.ProcessEnv = process.env): Config {
  return {
    port: Number(env["PORT"] ?? 5200),
    databasePath: env["RAILBITES_DB_PATH"] || "data/railbites.db",
    serviceKey: env["RAILBITES_SERVICE_KEY"] || undefined,
  };
}
