import type { IncomingMessage } from "node:http";
import { collectionPointName } from "./catalog";
import type { Config } from "./config";
import type { Db } from "./db";
import { ApiError, type Reply, type Router } from "./http";
import { listOrders, markCollected, markNoShow, markReady } from "./kiosk";
import { readForm, readJson } from "./parse";
import type { StaffSession } from "./sessions";
import { randomToken, type SessionStore } from "./sessions";
import { sameToken, verifyPassword } from "./staff-auth";
import { loginPage, staffOrdersContent, staffOrdersScript, staffPage } from "./staff-page";
import type { PassageReader } from "./smartrail";

export const SESSION_COOKIE = "railbites_kiosk_session";
export const LOGIN_CSRF_COOKIE = "railbites_kiosk_login";
const CSRF_HEADER = "x-railbites-csrf";
// The login CSRF cookie only needs to survive one form round-trip.
const LOGIN_CSRF_MAX_AGE_SECONDS = 600;

export interface StaffDeps {
  config: Config;
  db: Db;
  sessions: SessionStore;
  loginLimiter: RateLimiter;
  clock: () => Date;
  passage: PassageReader;
}

// Counts attempts per address in a sliding window. It protects the login route only; every other
// route needs a session or a credential already.
export class RateLimiter {
  private readonly hits = new Map<string, number[]>();

  constructor(
    private readonly max: number,
    private readonly windowMs: number,
  ) {}

  // True when the attempt is allowed, false when the caller has used up the window.
  allow(key: string, now: number): boolean {
    const cutoff = now - this.windowMs;
    const recent = (this.hits.get(key) ?? []).filter((at) => at > cutoff);
    if (recent.length >= this.max) {
      this.hits.set(key, recent);
      return false;
    }
    recent.push(now);
    this.hits.set(key, recent);
    return true;
  }
}

function cookies(req: IncomingMessage): Map<string, string> {
  const jar = new Map<string, string>();
  const header = req.headers.cookie;
  if (typeof header !== "string") return jar;
  for (const part of header.split(";")) {
    const at = part.indexOf("=");
    if (at < 0) continue;
    jar.set(part.slice(0, at).trim(), part.slice(at + 1).trim());
  }
  return jar;
}

function cookie(name: string, value: string, maxAgeSeconds: number, secure: boolean): string {
  return [
    `${name}=${value}`,
    "Path=/",
    "HttpOnly",
    "SameSite=Strict",
    `Max-Age=${Math.max(0, Math.floor(maxAgeSeconds))}`,
    ...(secure ? ["Secure"] : []),
  ].join("; ");
}

function redirect(location: string, status = 302): Reply {
  return { status, headers: { Location: location } };
}

// If Origin or Referer is present it must name this host. A browser always sends one for a
// cross-site POST; same-origin fetches and forms are accepted, and the CSRF token is still required.
function requireSameOrigin(req: IncomingMessage): void {
  const candidate =
    typeof req.headers.origin === "string" ? req.headers.origin : typeof req.headers.referer === "string" ? req.headers.referer : undefined;
  if (candidate === undefined) return;
  try {
    if (new URL(candidate).host !== req.headers.host) throw new Error("cross-origin");
  } catch {
    throw new ApiError(403, "FORBIDDEN_ORIGIN", "Request did not come from this page.");
  }
}

// The point comes from the session only; a URL or form never widens it.
function currentSession(deps: StaffDeps, req: IncomingMessage): StaffSession | undefined {
  const id = cookies(req).get(SESSION_COOKIE);
  if (!id) return undefined;
  return deps.sessions.get(id, deps.clock().getTime());
}

function requireSession(deps: StaffDeps, req: IncomingMessage): StaffSession {
  const session = currentSession(deps, req);
  if (!session) throw new ApiError(401, "NOT_SIGNED_IN", "Sign in to continue.");
  return session;
}

function requireCsrf(session: StaffSession, req: IncomingMessage): void {
  const provided = req.headers[CSRF_HEADER];
  if (typeof provided !== "string" || !sameToken(session.csrfToken, provided)) {
    throw new ApiError(403, "INVALID_CSRF", "Missing or invalid CSRF token.");
  }
}

export function registerStaffRoutes(router: Router, deps: StaffDeps): void {
  const secure = deps.config.staffSecureCookies;
  const now = (): Date => deps.clock();

  // The login page is also the answer to a failed sign-in: generic wording, fresh CSRF token.
  const renderLogin = (error: string | null, status: number): Reply => {
    const token = randomToken();
    const points = [...deps.config.kioskStaff.keys()].map((id) => ({ id, name: collectionPointName(deps.db, id) }));
    return {
      status,
      raw: loginPage({ csrfToken: token, points, error }),
      headers: {
        "Content-Type": "text/html; charset=utf-8",
        "Cache-Control": "no-store",
        "Set-Cookie": cookie(LOGIN_CSRF_COOKIE, token, LOGIN_CSRF_MAX_AGE_SECONDS, secure),
      },
    };
  };

  router.add("GET", "/staff/login", (ctx) => {
    if (currentSession(deps, ctx.req)) return redirect("/staff", 303);
    return renderLogin(null, 200);
  });

  router.add("POST", "/staff/login", async (ctx) => {
    const generic = "Sign-in failed.";
    const client = ctx.req.socket.remoteAddress ?? "unknown";
    if (!deps.loginLimiter.allow(client, now().getTime())) return renderLogin("Too many attempts. Try again later.", 429);
    requireSameOrigin(ctx.req);

    const form = await readForm(ctx.req);
    const csrfCookie = cookies(ctx.req).get(LOGIN_CSRF_COOKIE);
    const csrfField = form.get("csrf") ?? "";
    if (!csrfCookie || !sameToken(csrfCookie, csrfField)) return renderLogin("Your session expired. Try again.", 403);

    const point = (form.get("collectionPointId") ?? "").trim();
    const password = form.get("password") ?? "";
    const hash = deps.config.kioskStaff.get(point);
    // Same answer whether the point is unknown or the password is wrong.
    if (hash === undefined || !verifyPassword(password, hash)) return renderLogin(generic, 401);

    deps.sessions.sweep(now().getTime());
    // A login always mints a new id; any cookie the browser sent is retired.
    const previous = cookies(ctx.req).get(SESSION_COOKIE);
    if (previous) deps.sessions.destroy(previous);
    const session = deps.sessions.create(point, now().getTime(), deps.config.staffSessionLifetimeMs);

    return {
      status: 303,
      headers: {
        Location: "/staff",
        "Set-Cookie": [
          cookie(SESSION_COOKIE, session.id, deps.config.staffSessionLifetimeMs / 1000, secure),
          cookie(LOGIN_CSRF_COOKIE, "", 0, secure),
        ],
      },
    };
  });

  router.add("POST", "/staff/logout", async (ctx) => {
    const session = requireSession(deps, ctx.req);
    requireSameOrigin(ctx.req);
    // Accept the token in the header (fetch) or the form (the sign-out button).
    const provided =
      typeof ctx.req.headers[CSRF_HEADER] === "string" ? (ctx.req.headers[CSRF_HEADER] as string) : (await readForm(ctx.req)).get("csrf") ?? "";
    if (!sameToken(session.csrfToken, provided)) throw new ApiError(403, "INVALID_CSRF", "Missing or invalid CSRF token.");
    deps.sessions.destroy(session.id);
    return {
      status: 303,
      headers: { Location: "/staff/login", "Set-Cookie": cookie(SESSION_COOKIE, "", 0, secure) },
    };
  });

  router.add("GET", "/staff", (ctx) => {
    const session = currentSession(deps, ctx.req);
    if (!session) return redirect("/staff/login", 303);
    return {
      status: 200,
      raw: staffPage({
        pointName: collectionPointName(deps.db, session.collectionPointId),
        pointId: session.collectionPointId,
        csrfToken: session.csrfToken,
        content: staffOrdersContent(),
        script: staffOrdersScript(session.csrfToken),
      }),
      headers: { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store" },
    };
  });

  // The page polls this list. The point comes from the session, so another point's orders are absent.
  router.add("GET", "/staff/orders", (ctx) => {
    const session = requireSession(deps, ctx.req);
    return { ...listOrders(deps.db, session.collectionPointId, ctx.url.searchParams.get("status")), headers: { "Cache-Control": "no-store" } };
  });

  // Actions reuse the kiosk business functions with the session's point. They check the point, the
  // current state, the pickup code and (for no-show) SmartRail's passage read exactly as the kiosk
  // API does; they only add the session, same-origin and CSRF checks of the browser boundary.
  const order = "/staff/orders/:orderId";

  router.add("POST", `${order}/ready`, (ctx) => {
    const session = requireSession(deps, ctx.req);
    requireSameOrigin(ctx.req);
    requireCsrf(session, ctx.req);
    return markReady(deps.db, now(), session.collectionPointId, ctx.params["orderId"]!);
  });

  router.add("POST", `${order}/collected`, async (ctx) => {
    const session = requireSession(deps, ctx.req);
    requireSameOrigin(ctx.req);
    requireCsrf(session, ctx.req);
    return markCollected(deps.db, now(), session.collectionPointId, ctx.params["orderId"]!, await readJson(ctx.req));
  });

  router.add("POST", `${order}/no-show`, (ctx) => {
    const session = requireSession(deps, ctx.req);
    requireSameOrigin(ctx.req);
    requireCsrf(session, ctx.req);
    return markNoShow(deps.db, now, deps.passage, session.collectionPointId, ctx.params["orderId"]!);
  });
}
