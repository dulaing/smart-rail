import { ApiError } from "./http";
import { instant, object } from "./parse";

// A clock a test (or a local demo) can pin to an instant. It exists only when the process is started
// with RAILBITES_TEST_CLOCK=1 outside production, so a deployed service always uses real time.
export interface TestClock {
  now(): Date;
  set(value: string | null): void;
}

export function createTestClock(): TestClock {
  let pinned: Date | null = null;
  return {
    now: () => pinned ?? new Date(),
    set: (value) => {
      pinned = value === null ? null : new Date(value);
    },
  };
}

export function parseClockRequest(body: unknown): string | null {
  const now = object(body, "body")["now"];
  if (now === null) return null;
  if (now === undefined) throw new ApiError(400, "INVALID_REQUEST", "now: is required (an instant, or null for real time)");
  return instant(now, "now");
}
