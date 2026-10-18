import { createHash, timingSafeEqual } from "node:crypto";
import type { Config } from "./config";
import { ApiError, type Handler } from "./http";

export const SERVICE_KEY_HEADER = "x-railbites-service-key";
export const KIOSK_KEY_HEADER = "x-railbites-kiosk-key";

// Compare digests, not the keys, so the check takes the same time whatever the input length.
function sameKey(configured: string, provided: string): boolean {
  const a = createHash("sha256").update(configured).digest();
  const b = createHash("sha256").update(provided).digest();
  return timingSafeEqual(a, b);
}

// Service routes are for SmartRail only. With no configured key, every call is refused.
export function requireService(config: Config, handler: Handler): Handler {
  return (ctx) => {
    const provided = ctx.req.headers[SERVICE_KEY_HEADER];
    if (!config.serviceKey || typeof provided !== "string" || !sameKey(config.serviceKey, provided)) {
      throw new ApiError(401, "UNAUTHORIZED", "A valid service credential is required.");
    }
    return handler(ctx);
  };
}

// A kiosk key belongs to one collection point. An unknown key is 401; a real key used on another
// point's routes is 403. The service key never opens kiosk routes.
export function requireKiosk(config: Config, handler: Handler): Handler {
  return (ctx) => {
    const provided = ctx.req.headers[KIOSK_KEY_HEADER];
    if (typeof provided !== "string") throw new ApiError(401, "UNAUTHORIZED", "A valid kiosk credential is required.");

    // Check every configured key so the work done does not reveal which one matched.
    let owner: string | undefined;
    for (const [collectionPointId, key] of config.kioskKeys) {
      if (sameKey(key, provided)) owner = collectionPointId;
    }
    if (owner === undefined) throw new ApiError(401, "UNAUTHORIZED", "A valid kiosk credential is required.");
    if (owner !== ctx.params["collectionPointId"]) {
      throw new ApiError(403, "FORBIDDEN_COLLECTION_POINT", "This kiosk credential is for another collection point.");
    }
    return handler(ctx);
  };
}
