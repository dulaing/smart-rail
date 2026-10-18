import type { Config } from "./config";
import type { Db } from "./db";
import { EVENT_ID_HEADER, SIGNATURE_HEADER, TIMESTAMP_HEADER, sign } from "./signature";

interface OutboxRow {
  event_id: string;
  payload: string;
  attempt_count: number;
}

export interface CallbackPass {
  attempted: number;
  delivered: number;
}

const BATCH_SIZE = 20;
const REQUEST_TIMEOUT_MS = 5_000;

// Increasing delays after a timeout or 5xx: 2, 4, 8 ... seconds, never more than the configured cap.
const backoffSeconds = (attempt: number, cap: number): number => Math.min(cap, 2 ** Math.min(attempt, 16));

// Sends each queued status callback to SmartRail, oldest first. A callback counts as delivered only
// when SmartRail answers 200, which it does after its own transaction has committed, so a crash on
// either side in between just causes a repeat that SmartRail recognises. Every attempt signs the
// stored body again with the current time; the event id and the body bytes never change. A refusal
// SmartRail will not change its mind about (bad signature, unknown order) is kept and retried slowly,
// so correcting a secret gets it through without anyone editing a row. Never logs a body or signature.
export async function deliverDueCallbacks(
  db: Db,
  config: Config,
  now: Date,
  send: typeof fetch = fetch,
): Promise<CallbackPass> {
  if (!config.smartRailBaseUrl || !config.callbackSecret) {
    // Fail closed: with no address or no secret nothing is sent and nothing is lost.
    return { attempted: 0, delivered: 0 };
  }

  const due = db
    .prepare(
      `SELECT event_id, payload, attempt_count FROM webhook_outbox
       WHERE delivered_at IS NULL AND next_attempt_at <= ? ORDER BY created_at, rowid LIMIT ?`,
    )
    .all(now.toISOString(), BATCH_SIZE) as unknown as OutboxRow[];

  const url = new URL("/internal/catering/order-updates", config.smartRailBaseUrl);
  let delivered = 0;
  for (const row of due) {
    const timestamp = Math.floor(now.getTime() / 1000);
    let outcome: "delivered" | "transient" | "permanent";
    let error: string | null = null;
    try {
      const response = await send(url, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          [TIMESTAMP_HEADER]: String(timestamp),
          [SIGNATURE_HEADER]: sign(config.callbackSecret, timestamp, row.payload),
          [EVENT_ID_HEADER]: row.event_id,
        },
        body: row.payload,
        signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
      });
      if (response.status === 200) {
        const receipt = (await response.json()) as { result?: unknown } | null;
        if (receipt && ["APPLIED", "DUPLICATE", "STALE"].includes(String(receipt.result))) {
          outcome = "delivered";
        } else {
          outcome = "transient";
          error = "INVALID_ACKNOWLEDGEMENT";
        }
      } else if (response.status >= 500 || response.status === 408 || response.status === 429) {
        outcome = "transient";
        error = `HTTP_${response.status}`;
        await response.body?.cancel();
      } else if (response.ok) {
        outcome = "transient";
        error = "INVALID_ACKNOWLEDGEMENT";
        await response.body?.cancel();
      } else {
        outcome = "permanent";
        error = `HTTP_${response.status}`;
        await response.body?.cancel();
      }
    } catch {
      outcome = "transient";
      error = "UNREACHABLE";
    }

    if (outcome === "delivered") {
      db.prepare(
        "UPDATE webhook_outbox SET delivered_at = ?, last_error = NULL, attempt_count = attempt_count + 1 WHERE event_id = ? AND delivered_at IS NULL",
      ).run(now.toISOString(), row.event_id);
      delivered += 1;
      continue;
    }

    const wait = outcome === "permanent" ? config.callbackFailedRetrySeconds : backoffSeconds(row.attempt_count + 1, config.callbackMaxBackoffSeconds);
    db.prepare("UPDATE webhook_outbox SET attempt_count = attempt_count + 1, next_attempt_at = ?, last_error = ? WHERE event_id = ?").run(
      new Date(now.getTime() + wait * 1000).toISOString(),
      error,
      row.event_id,
    );
  }

  return { attempted: due.length, delivered };
}

// The background loop. Its queue is the webhook_outbox table, so stopping and starting the process
// loses nothing. Passes never overlap.
export function startCallbackLoop(db: Db, config: Config, clock: () => Date): () => void {
  if (config.callbackPollMs <= 0) return () => undefined;

  let running = false;
  const timer = setInterval(() => {
    if (running) return;
    running = true;
    deliverDueCallbacks(db, config, clock())
      .catch((error: unknown) => console.error(`Callback pass failed: ${error instanceof Error ? error.name : "unknown"}`))
      .finally(() => {
        running = false;
      });
  }, config.callbackPollMs);
  timer.unref();
  return () => clearInterval(timer);
}
