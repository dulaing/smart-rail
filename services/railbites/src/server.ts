import { createServer, type Server } from "node:http";

// Small HTTP server. Health only says the process started; it checks no dependencies.
export function createApp(): Server {
  return createServer((req, res) => {
    if (req.method === "GET" && req.url === "/health/live") {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ status: "Healthy" }));
      return;
    }

    res.writeHead(404, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ error: "Not found" }));
  });
}
