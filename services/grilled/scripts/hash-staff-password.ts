import { hashPassword } from "../src/staff-auth";

// Provision a shared staff login for one collection point without ever printing the password.
//
//   printf '%s' "$PASSWORD" | npx tsx scripts/hash-staff-password.ts --point polgahawela-junction
//
// The password is read from standard input so it stays out of the shell history and this script's
// output. The printed value goes into GRILLED_KIOSK_STAFF (or the matching Aspire secret), never
// into Git. Repeat for each collection point and join the entries with commas.

async function readPassword(): Promise<string> {
  const chunks: Buffer[] = [];
  for await (const chunk of process.stdin) chunks.push(chunk as Buffer);
  // Drop one trailing newline so `echo` and `printf` behave the same.
  return Buffer.concat(chunks).toString("utf8").replace(/\r?\n$/, "");
}

async function main(): Promise<void> {
  const pointIndex = process.argv.indexOf("--point");
  const point = pointIndex >= 0 ? (process.argv[pointIndex + 1] ?? "").trim() : "";
  if (pointIndex >= 0 && point === "") {
    console.error("--point needs a collection point id.");
    process.exit(2);
  }

  const password = await readPassword();
  if (password === "") {
    console.error("No password on standard input.");
    process.exit(2);
  }

  const hash = hashPassword(password);
  if (point === "") {
    console.log(hash);
    return;
  }
  console.log(`${point}=${hash}`);
  console.log("");
  console.error(`Add the line above to GRILLED_KIOSK_STAFF (entries are comma-separated).`);
}

void main();
