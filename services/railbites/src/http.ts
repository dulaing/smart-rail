import type { IncomingMessage, ServerResponse } from "node:http";

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
  headers?: Record<string, string>;
}

export type Handler = (ctx: RequestContext) => Reply | Promise<Reply>;

interface Route {
  method: string;
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
    this.routes.push({ method, pattern: new RegExp(`^${source}$`), names, handler });
  }

  async handle(req: IncomingMessage, res: ServerResponse): Promise<void> {
    try {
      send(res, await this.dispatch(req));
    } catch (error) {
      if (error instanceof ApiError) {
        send(res, {
          status: error.status,
          body: { code: error.code, message: error.message, ...(error.details === undefined ? {} : { details: error.details }) },
        });
        return;
      }
      // Never echo internals (they could carry pickup codes or secrets); log only the error type.
      console.error(`Unhandled error: ${error instanceof Error ? error.name : "unknown"}`);
      send(res, { status: 500, body: { code: "INTERNAL_ERROR", message: "Unexpected error." } });
    }
  }

  private async dispatch(req: IncomingMessage): Promise<Reply> {
    const url = new URL(req.url ?? "/", "http://railbites.local");
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
      return route.handler({ req, url, params });
    }

    if (pathMatched) throw new ApiError(405, "METHOD_NOT_ALLOWED", "Method not allowed.");
    throw new ApiError(404, "NOT_FOUND", "Not found.");
  }
}

function send(res: ServerResponse, reply: Reply): void {
  const payload = reply.body === undefined ? undefined : JSON.stringify(reply.body);
  res.writeHead(reply.status, {
    ...(payload === undefined ? {} : { "Content-Type": "application/json" }),
    ...reply.headers,
  });
  res.end(payload);
}
