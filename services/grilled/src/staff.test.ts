import assert from "node:assert/strict";
import { after, before, test } from "node:test";
import type { Config } from "./config";
import {
  KIOSK_KEYS,
  KIOSK_STAFF,
  SERVICE_KEY,
  STAFF_PASSWORD,
  startApp,
  temporaryDatabasePath,
  type TestApp,
} from "./test-support";

const database = temporaryDatabasePath();
let app: TestApp;

before(async () => {
  app = await startApp({ databasePath: database.path });
});

after(async () => {
  await app.close();
  database.remove();
});

function csrfFrom(html: string): string {
  const match = /name="csrf" value="([^"]+)"/.exec(html);
  assert.ok(match, "the form should carry a CSRF token");
  return match[1]!;
}

function cookieFrom(response: Response, name: string): string | undefined {
  const raw = response.headers.getSetCookie().find((value) => value.startsWith(`${name}=`));
  return raw === undefined ? undefined : raw.split(";")[0];
}

async function loginPage(baseUrl: string): Promise<{ status: number; html: string; cookie: string }> {
  const res = await fetch(`${baseUrl}/staff/login`, { redirect: "manual" });
  const html = await res.text();
  return { status: res.status, html, cookie: cookieFrom(res, "grilled_kiosk_login") ?? "" };
}

// Full sign-in dance: fetch the form token, then post the form with the cookie that owns it.
async function signIn(
  baseUrl: string,
  point = "peradeniya-junction",
  password = STAFF_PASSWORD,
  overrides: { csrf?: string; cookie?: string; referer?: string } = {},
): Promise<{ status: number; sessionCookie: string | undefined }> {
  const page = await loginPage(baseUrl);
  const csrf = overrides.csrf ?? csrfFrom(page.html);
  const cookie = overrides.cookie ?? page.cookie;
  const body = new URLSearchParams({ collectionPointId: point, password, csrf });
  const res = await fetch(`${baseUrl}/staff/login`, {
    method: "POST",
    redirect: "manual",
    headers: { "Content-Type": "application/x-www-form-urlencoded", Cookie: cookie, ...(overrides.referer ? { Referer: overrides.referer } : {}) },
    body,
  });
  return { status: res.status, sessionCookie: cookieFrom(res, "grilled_kiosk_session") };
}

test("the login page is served without a session and reveals no internal key", async () => {
  const page = await loginPage(app.baseUrl);
  assert.equal(page.status, 200);
  assert.match(page.html, /Grilled staff/);
  assert.match(page.html, /Polgahawela Junction/);
  for (const secret of [SERVICE_KEY, ...KIOSK_KEYS.values(), ...KIOSK_STAFF.values()]) {
    assert.ok(!page.html.includes(secret), "the page must not contain a configured secret");
  }
  assert.ok(!page.html.includes("localStorage"), "the page must not store anything locally");
});

test("a valid shared password signs in and fixes the collection point on the server", async () => {
  const { status, sessionCookie } = await signIn(app.baseUrl, "polgahawela-junction");
  assert.equal(status, 303);
  assert.ok(sessionCookie, "a session cookie should be set");

  const res = await fetch(`${app.baseUrl}/staff`, { headers: { Cookie: sessionCookie! }, redirect: "manual" });
  assert.equal(res.status, 200);
  const html = await res.text();
  assert.match(html, /Polgahawela Junction/);
  assert.ok(!html.includes("Peradeniya"), "the page shows only the session's point");
  for (const secret of [SERVICE_KEY, ...KIOSK_KEYS.values()]) {
    assert.ok(!html.includes(secret));
  }
});

test("the login form's collection point cannot widen the session: an unknown point is refused", async () => {
  const { status, sessionCookie } = await signIn(app.baseUrl, "colombo-fort");
  assert.equal(status, 401);
  assert.equal(sessionCookie, undefined);
});

test("a wrong password fails generically, the same as an unknown point", async () => {
  const wrong = await signIn(app.baseUrl, "peradeniya-junction", "not-the-password");
  assert.equal(wrong.status, 401);
  const unknown = await signIn(app.baseUrl, "nowhere", "not-the-password");
  assert.equal(unknown.status, 401);
});

test("a missing or mismatched CSRF token is refused", async () => {
  const page = await loginPage(app.baseUrl);
  const body = new URLSearchParams({ collectionPointId: "peradeniya-junction", password: STAFF_PASSWORD, csrf: "forged" });
  const res = await fetch(`${app.baseUrl}/staff/login`, {
    method: "POST",
    redirect: "manual",
    headers: { "Content-Type": "application/x-www-form-urlencoded", Cookie: page.cookie },
    body,
  });
  assert.equal(res.status, 403);
});

test("a cross-origin sign-in is refused before anything else", async () => {
  const { status } = await signIn(app.baseUrl, "peradeniya-junction", STAFF_PASSWORD, { referer: "https://evil.example/staff/login" });
  assert.equal(status, 403);
});

test("sign-in attempts are rate limited", async () => {
  const limited = await startApp({ databasePath: temporaryDatabasePath().path, overrides: { staffLoginMaxAttempts: 2 } });
  try {
    assert.equal((await signIn(limited.baseUrl, "peradeniya-junction", "wrong")).status, 401);
    assert.equal((await signIn(limited.baseUrl, "peradeniya-junction", "wrong")).status, 401);
    assert.equal((await signIn(limited.baseUrl, "peradeniya-junction", STAFF_PASSWORD)).status, 429);
  } finally {
    await limited.close();
  }
});

test("logout needs the session CSRF token, then the cookie stops working", async () => {
  const { sessionCookie } = await signIn(app.baseUrl);
  assert.ok(sessionCookie);
  const page = await fetch(`${app.baseUrl}/staff`, { headers: { Cookie: sessionCookie! } });
  const csrf = csrfFrom(await page.text());

  const refused = await fetch(`${app.baseUrl}/staff/logout`, {
    method: "POST",
    redirect: "manual",
    headers: { "Content-Type": "application/x-www-form-urlencoded", Cookie: sessionCookie! },
    body: new URLSearchParams({ csrf: "forged" }),
  });
  assert.equal(refused.status, 403);

  const done = await fetch(`${app.baseUrl}/staff/logout`, {
    method: "POST",
    redirect: "manual",
    headers: { "Content-Type": "application/x-www-form-urlencoded", Cookie: sessionCookie! },
    body: new URLSearchParams({ csrf }),
  });
  assert.equal(done.status, 303);
  assert.equal(done.headers.get("Location"), "/staff/login");

  const after = await fetch(`${app.baseUrl}/staff`, { headers: { Cookie: sessionCookie! }, redirect: "manual" });
  assert.equal(after.status, 303);
  assert.equal(after.headers.get("Location"), "/staff/login");
});

test("a session expires after its configured lifetime", async () => {
  const expiring = await startApp({ databasePath: temporaryDatabasePath().path, now: "2026-10-18T05:00:00.000Z", overrides: { staffSessionLifetimeMs: 1_000 } });
  try {
    const { sessionCookie } = await signIn(expiring.baseUrl);
    assert.ok(sessionCookie);
    expiring.setNow("2026-10-18T05:00:30.000Z");
    const res = await fetch(`${expiring.baseUrl}/staff`, { headers: { Cookie: sessionCookie! }, redirect: "manual" });
    assert.equal(res.status, 303);
    assert.equal(res.headers.get("Location"), "/staff/login");
  } finally {
    await expiring.close();
  }
});

test("sessions do not survive a restart", async () => {
  const path = temporaryDatabasePath();
  const first = await startApp({ databasePath: path.path });
  const { sessionCookie } = await signIn(first.baseUrl);
  assert.ok(sessionCookie);
  await first.close();

  const second = await startApp({ databasePath: path.path });
  try {
    const res = await fetch(`${second.baseUrl}/staff`, { headers: { Cookie: sessionCookie! }, redirect: "manual" });
    assert.equal(res.status, 303);
    assert.equal(res.headers.get("Location"), "/staff/login");
  } finally {
    await second.close();
    path.remove();
  }
});

test("with no staff configured the login cannot be used at all", async () => {
  const overrides: Partial<Config> = { kioskStaff: new Map() };
  const closed = await startApp({ databasePath: temporaryDatabasePath().path, overrides });
  try {
    const page = await loginPage(closed.baseUrl);
    assert.equal(page.status, 200);
    assert.match(page.html, /No collection-point sign-in is configured/);

    // With no form there is no hidden field, so the double-submit token comes from the cookie.
    const csrf = page.cookie.split("=")[1] ?? "";
    const body = new URLSearchParams({ collectionPointId: "peradeniya-junction", password: STAFF_PASSWORD, csrf });
    const res = await fetch(`${closed.baseUrl}/staff/login`, {
      method: "POST",
      redirect: "manual",
      headers: { "Content-Type": "application/x-www-form-urlencoded", Cookie: page.cookie },
      body,
    });
    assert.equal(res.status, 401);
  } finally {
    await closed.close();
  }
});

test("the session cookie is HttpOnly and SameSite=Strict", async () => {
  const page = await loginPage(app.baseUrl);
  const csrf = csrfFrom(page.html);
  const res = await fetch(`${app.baseUrl}/staff/login`, {
    method: "POST",
    redirect: "manual",
    headers: { "Content-Type": "application/x-www-form-urlencoded", Cookie: page.cookie },
    body: new URLSearchParams({ collectionPointId: "peradeniya-junction", password: STAFF_PASSWORD, csrf }),
  });
  const raw = res.headers.getSetCookie().find((value) => value.startsWith("grilled_kiosk_session="))!;
  assert.match(raw, /HttpOnly/);
  assert.match(raw, /SameSite=Strict/);
  assert.ok(!/Secure/.test(raw), "local test config omits Secure only because it was explicitly disabled");
});
