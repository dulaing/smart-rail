import type { Config } from "./config";

export type PassageState = "AHEAD" | "PASSED" | "UNKNOWN";

// RailBites' one authenticated read of SmartRail: has the train passed this pickup stop occurrence?
export interface PassageReader {
  read(journeyId: string, stopId: string): Promise<PassageState>;
}

const REQUEST_TIMEOUT_MS = 5_000;

export function httpPassageReader(config: Config): PassageReader {
  return {
    async read(journeyId, stopId) {
      if (!config.smartRailBaseUrl || !config.smartRailServiceKey) {
        throw new Error("SmartRail address or credential is not configured.");
      }
      const url = new URL(
        `/internal/catering/journeys/${encodeURIComponent(journeyId)}/stops/${encodeURIComponent(stopId)}/passage`,
        config.smartRailBaseUrl,
      );
      const res = await fetch(url, {
        headers: { "X-RailBites-Service-Key": config.smartRailServiceKey },
        signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
      });
      if (!res.ok) throw new Error(`SmartRail answered ${res.status}.`);
      const state = ((await res.json()) as { state?: unknown }).state;
      if (state !== "AHEAD" && state !== "PASSED" && state !== "UNKNOWN") throw new Error("Unexpected passage answer.");
      return state;
    },
  };
}
