import { loadConfig } from "./config";
import { openDatabase } from "./db";
import { migrate } from "./migrations";
import { seedCatalog } from "./seed";
import { createApp } from "./server";

const config = loadConfig();
if (!config.serviceKey) {
  console.warn("RAILBITES_SERVICE_KEY is not set: every service route will refuse requests.");
}

const db = openDatabase(config.databasePath);
migrate(db);
seedCatalog(db);

const server = createApp({ config, db }).listen(config.port, () => {
  console.log(`RailBites listening on http://localhost:${config.port} (database ${config.databasePath})`);
});

// Stop accepting requests, then close SQLite so the WAL is checkpointed.
function shutdown(): void {
  server.close(() => {
    db.close();
    process.exit(0);
  });
  server.closeAllConnections();
}
process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);
