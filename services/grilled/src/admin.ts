import type { Db } from "./db";
import { ApiError, type Reply } from "./http";
import * as parse from "./parse";

// The ordering cutoff belongs to Grilled and is changed here, not in SmartRail: SmartRail reads it
// fresh on every browse and never caches it, so the change is live for the next order and cancellation
// without any SmartRail release. Orders already accepted keep their place.
export function setCutoff(db: Db, collectionPointId: string, body: unknown): Reply {
  const cutoffMinutes = parse.integer(parse.object(body, "body")["cutoffMinutes"], "cutoffMinutes", 0, 240);
  const changed = db.prepare("UPDATE collection_points SET cutoff_minutes = ? WHERE id = ? AND active = 1").run(cutoffMinutes, collectionPointId);
  if (changed.changes === 0) throw new ApiError(404, "COLLECTION_POINT_NOT_FOUND", "Unknown collection point.");
  return { status: 200, body: { id: collectionPointId, cutoffMinutes } };
}
