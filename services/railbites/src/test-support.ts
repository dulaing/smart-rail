import { mkdtempSync, rmSync } from "node:fs";
import type { AddressInfo } from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";
import type { Config } from "./config";
import { openDatabase, type Db } from "./db";
import { migrate } from "./migrations";
import { seedCatalog } from "./seed";
import { createApp } from "./server";

export const SERVICE_KEY = "test-service-key";

export interface TestApp {
  baseUrl: string;
  db: Db;
  databasePath: string;
  close(): Promise<void>;
}

export function temporaryDatabasePath(): { path: string; remove(): void } {
  const dir = mkdtempSync(join(tmpdir(), "railbites-"));
  return { path: join(dir, "railbites.db"), remove: () => rmSync(dir, { recursive: true, force: true }) };
}

// Starts the real app on a random port over a file-backed SQLite database, like production does.
export async function startApp(options: { databasePath: string; serviceKey?: string | undefined }): Promise<TestApp> {
  const config: Config = {
    port: 0,
    databasePath: options.databasePath,
    serviceKey: "serviceKey" in options ? options.serviceKey : SERVICE_KEY,
  };
  const db = openDatabase(config.databasePath);
  migrate(db);
  seedCatalog(db);

  const server = createApp({ config, db });
  await new Promise<void>((resolve) => server.listen(0, resolve));
  return {
    baseUrl: `http://localhost:${(server.address() as AddressInfo).port}`,
    db,
    databasePath: config.databasePath,
    close: async () => {
      await new Promise<void>((resolve) => {
        server.close(() => resolve());
        server.closeAllConnections();
      });
      db.close();
    },
  };
}

export function serviceHeaders(key: string = SERVICE_KEY): Record<string, string> {
  return { "X-RailBites-Service-Key": key };
}
