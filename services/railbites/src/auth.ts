import { createHash, timingSafeEqual } from "node:crypto";
import type { Config } from "./config";
import { ApiError, type Handler } from "./http";

export const SERVICE_KEY_HEADER = "x-railbites-service-key";

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
