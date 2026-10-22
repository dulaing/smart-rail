import { startCallbackLoop } from "./callbacks";
import { startTelemetry } from "./telemetry";
import { loadConfig } from "./config";
import { openDatabase } from "./db";
import { migrate } from "./migrations";
import { seedCatalog } from "./seed";
import { createApp } from "./server";
import { createTestClock } from "./test-clock";

const telemetry = startTelemetry();
const config = loadConfig();
if (!config.serviceKey) {
  console.warn("GRILLED_SERVICE_KEY is not set: every service route will refuse requests.");
}

const db = openDatabase(config.databasePath);
migrate(db);
seedCatalog(db);

const dev = process.env["GRILLED_DEV_CONTROLS"] === "1" && process.env["NODE_ENV"] !== "production" ? { outage: false } : undefined;
const testClock = process.env["GRILLED_TEST_CLOCK"] === "1" && process.env["NODE_ENV"] !== "production" ? createTestClock() : undefined;
const server = createApp({ config, db, ...(testClock ? { testClock } : {}), ...(dev ? { dev } : {}) }).listen(config.port, () => {
  console.log(`Grilled listening on http://localhost:${config.port} (database ${config.databasePath})`);
});

const stopCallbacks = startCallbackLoop(db, config, () => (testClock ? testClock.now() : new Date()), () => dev?.outage ?? false);

// Stop accepting requests, then close SQLite so the WAL is checkpointed.
function shutdown(): void {
  stopCallbacks();
  server.close(() => {
    db.close();
    void telemetry.shutdown().finally(() => process.exit(0));
  });
  server.closeAllConnections();
}
process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);
