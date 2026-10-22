import type { IncomingMessage, ServerResponse } from "node:http";
import { SpanKind, SpanStatusCode, withSpan } from "./telemetry";

// Every error leaves the service in one shape: { code, message, details? }. `code` is stable and
// meant for programs; `message` is for people.
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
    readonly details?: unknown,
  ) {
    super(message);
  }
}

export interface RequestContext {
  req: IncomingMessage;
  url: URL;
  params: Record<string, string>;
}

export interface Reply {
  status: number;
  body?: unknown;
  /** A ready-to-send body (such as an HTML page) that is not JSON and must not be serialised. */
  raw?: string;
  headers?: Record<string, string | string[]>;
}

export type Handler = (ctx: RequestContext) => Reply | Promise<Reply>;

interface Route {
  method: string;
  path: string;
  pattern: RegExp;
  names: string[];
  handler: Handler;
}

export class Router {
  private readonly routes: Route[] = [];

  add(method: string, path: string, handler: Handler): void {
    const names: string[] = [];
    const source = path.replace(/:([A-Za-z]+)/g, (_, name: string) => {
      names.push(name);
      return "([^/]+)";
    });
    this.routes.push({ method, path, pattern: new RegExp(`^${source}$`), names, handler });
  }

  // One server span per request, continuing the caller's trace from its `traceparent` header. The
  // route pattern names the span, never the concrete path, so ids and codes stay out of telemetry.
  async handle(req: IncomingMessage, res: ServerResponse): Promise<void> {
    const traceparent = typeof req.headers["traceparent"] === "string" ? req.headers["traceparent"] : undefined;
    const route = { name: "unmatched" };
    await withSpan(
      `${req.method} request`,
      traceparent,
      { "http.request.method": req.method ?? "" },
      async (span) => {
        const reply = await this.reply(req, route);
        span.updateName(`${req.method} ${route.name}`);
        span.setAttribute("http.route", route.name);
        span.setAttribute("http.response.status_code", reply.status);
        if (reply.status >= 500) span.setStatus({ code: SpanStatusCode.ERROR });
        send(res, reply);
      },
      SpanKind.SERVER,
    );
  }

  private async reply(req: IncomingMessage, route: { name: string }): Promise<Reply> {
    try {
      return await this.dispatch(req, route);
    } catch (error) {
      if (error instanceof ApiError) {
        return {
          status: error.status,
          body: { code: error.code, message: error.message, ...(error.details === undefined ? {} : { details: error.details }) },
        };
      }
      // Never echo internals (they could carry pickup codes or secrets); log only the error type.
      console.error(`Unhandled error: ${error instanceof Error ? error.name : "unknown"}`);
      return { status: 500, body: { code: "INTERNAL_ERROR", message: "Unexpected error." } };
    }
  }

  private async dispatch(req: IncomingMessage, found: { name: string }): Promise<Reply> {
    const url = new URL(req.url ?? "/", "http://grilled.local");
    let pathMatched = false;
    for (const route of this.routes) {
      const match = route.pattern.exec(url.pathname);
      if (!match) continue;
      pathMatched = true;
      if (route.method !== req.method) continue;

      const params: Record<string, string> = {};
      try {
        route.names.forEach((name, index) => {
          params[name] = decodeURIComponent(match[index + 1]!);
        });
      } catch {
        throw new ApiError(400, "INVALID_REQUEST", "Malformed path.");
      }
      found.name = route.path;
      return route.handler({ req, url, params });
    }

    if (pathMatched) throw new ApiError(405, "METHOD_NOT_ALLOWED", "Method not allowed.");
    throw new ApiError(404, "NOT_FOUND", "Not found.");
  }
}

function send(res: ServerResponse, reply: Reply): void {
  if (reply.raw !== undefined) {
    res.writeHead(reply.status, reply.headers);
    res.end(reply.raw);
    return;
  }
  const payload = reply.body === undefined ? undefined : JSON.stringify(reply.body);
  res.writeHead(reply.status, {
    ...(payload === undefined ? {} : { "Content-Type": "application/json" }),
    ...reply.headers,
  });
  res.end(payload);
}
