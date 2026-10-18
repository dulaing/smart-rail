# Running the services locally

Requires the .NET 10 SDK, Node 24 and Docker.

## Whole stack (Aspire)

    dotnet run --project services/apphost

This starts PostgreSQL, Keycloak, the API and RailBites, and opens the Aspire dashboard
(URL and login link are printed in the console). Stop it with Ctrl+C.

- Keycloak: `https://localhost:8080`, with a certificate from the SmartRail development CA (see
  "Local HTTPS" below). Token issuer: `https://localhost:8080/realms/smartrail`.
  The admin password is in the dashboard under the keycloak resource.
- API: `http://localhost:5111/health/live` and `/health/ready` (ready checks the database).
- RailBites: its port is shown in the dashboard; `/health/live`.
- Database data lives in the Docker volume `smartrail-postgres-data`. Remove it to start fresh.

## One service alone

    dotnet run --project services/api                 # http://localhost:5111 with the http profile
    npm --prefix services/railbites install
    npm --prefix services/railbites run dev           # http://localhost:5200

Without the AppHost the API has no database, so `/health/ready` reports healthy with no checks.

## RailBites (food ordering provider)

RailBites is a separate Node service with its own SQLite file; its HTTP contract is
`services/railbites/openapi.yaml`. It uses Node's built-in `node:sqlite`, so there is nothing native to
build (Node may print an experimental-feature warning).

| Setting | Default | Meaning |
| --- | --- | --- |
| `RAILBITES_DB_PATH` | `data/railbites.db` | SQLite file. The AppHost sets it to `services/railbites/data/railbites.db`; delete that folder to start fresh. The Docker image uses `/data/railbites.db`, which must be a mounted volume. |
| `RAILBITES_SERVICE_KEY` | none | Credential SmartRail sends as `X-RailBites-Service-Key`. With none set every service route answers 401. |
| `RAILBITES_KIOSK_KEYS` | none | `point-id=key,point-id=key`. Each key opens only its own collection point's kiosk routes, sent as `X-RailBites-Kiosk-Key`. |
| `SMARTRAIL_BASE_URL` | none | SmartRail API address. RailBites asks it whether a train has passed a stop before marking a no-show. |
| `SMARTRAIL_SERVICE_KEY` | none | RailBites' own credential for that read; not the same secret as `RAILBITES_SERVICE_KEY`. |
| `PORT` | `5200` | HTTP port. |

Start-up creates the tables (`schema_migrations` records what ran) and seeds the two collection
points and their menus. Re-seeding on every start never resets stock or the cutoff.

    RAILBITES_SERVICE_KEY=demo-key npm --prefix services/railbites run dev
    curl -H "X-RailBites-Service-Key: demo-key" http://localhost:5200/v1/collection-points

## Simulated running (demo trains)

The API can pretend the fictional seeded trains are running: every 30 seconds it reports where
each one is, through the same checks a real feed would pass. It is off by default. Guests read the
result at `GET /api/journeys/{journeyId}/running`; a train that is not reporting becomes
**unknown** after four minutes.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Observations:ServiceKey` | none | Shared key for `POST /internal/rail/observations` and the pause/resume commands, sent as `X-SmartRail-Service-Key`. With no key set every such call is refused. |
| `Simulator:Enabled` | `false` | Turns the 30-second simulator on. |
| `Simulator:DemoDelaySeconds` | `0` | Runs every simulated train this many seconds late (never early). Must not be negative. |

Short local run (with the stack from "Whole stack (Aspire)" and the database prepared as in
"One-time local setup"):

    dotnet user-secrets set Observations:ServiceKey demo-key --project services/api
    dotnet user-secrets set Simulator:Enabled true --project services/api
    dotnet run --project services/apphost

    # Find a journey that is running now (see the times below), then watch it:
    curl "http://localhost:5111/api/journeys?serviceDate=2026-10-16"
    curl http://localhost:5111/api/journeys/<journeyId>/running

    # Make one train go silent, and bring it back (Development only):
    curl -X POST -H "X-SmartRail-Service-Key: demo-key" http://localhost:5111/internal/rail/simulator/journeys/<journeyId>/pause
    curl -X POST -H "X-SmartRail-Service-Key: demo-key" http://localhost:5111/internal/rail/simulator/journeys/<journeyId>/resume

A train reports only while it is in service (Colombo time): `SIM-1003` 07:20-08:15, `SIM-1001`
07:35-08:30, `SIM-1002` 08:05-09:00, `SIM-2001` 17:10-18:30, `SIM-3001` 09:00-11:50 and `SIM-9001` 23:45-00:45. It reports
nothing before it first departs and stops after it reaches its last stop; at a stop it waits at
progress 1000 until the scheduled departure. Pausing stops that journey's reports but not the clock,
so it goes ageing after three minutes and unknown after four; resuming reports where the train is
now and does not replay the missed 30-second steps. Reports already stored are never deleted. Pauses
are kept in memory, so restarting the API resumes every train.

## Food ordering through SmartRail

Passengers order through the API; the API talks to RailBites. Browsing is public: `GET
/api/journeys/{journeyId}/collection-options` and `GET /api/food/collection-points/{id}/menu`. Ordering
needs a signed-in passenger: `POST /api/food/orders`, `GET /api/food/orders`, `GET
/api/food/orders/{id}` and `POST /api/food/orders/{id}/cancellation`. A `202` means RailBites has not
answered yet; poll the `Location`. The API retries a pending order in the background with the same
link ID until the provider answers.

| Setting | Default | Meaning |
| --- | --- | --- |
| `RailBites:BaseUrl` | none | RailBites address, for example `http://localhost:5200`. |
| `RailBites:ServiceKey` | none | Must equal RailBites' `RAILBITES_SERVICE_KEY`. With either unset nothing is sent and orders stay pending. |
| `RailBites:TimeoutSeconds` | `5` | How long one provider call may take before the order is treated as pending. |
| `Catering:Placement:PollSeconds` | `5` | How often the retry loop looks for pending orders. |
| `Catering:Placement:SettleSeconds` | `30` | After a lost request, how long to wait before a missing provider order is called refused. |
| `Catering:Placement:WorkerEnabled` | `true` | Turns the retry loop off (tests run it by hand). |
| `Catering:Outbox:PollSeconds` | `5` | How often confirmed train changes are sent to RailBites. |
| `Catering:Outbox:MaxBackoffSeconds` | `300` | Longest wait between retries after a timeout or 503 (waits grow 2, 4, 8 ... seconds). |
| `Catering:Outbox:FailedRetrySeconds` | `600` | Wait before retrying a change RailBites refused or a rejected credential; the change is kept, never dropped. |
| `Catering:Outbox:WorkerEnabled` | `true` | Turns the delivery loop off (tests run it by hand). |

A confirmed delay, cancellation, early ending or withdrawal is saved with its audit row and a message
for RailBites in one PostgreSQL transaction, so RailBites can be down without losing it. The message
carries the post-change eligibility and every stop's pickup time. `dotnet run --project services/api -- --migrate-and-seed`
also rewrites any undelivered message queued before pickup times existed (it is never marked delivered by that step).

The corridor train `SIM-3001` (Fort 09:00, Polgahawela 11:00, Peradeniya 11:50, daily) is seeded for
food ordering. The API tests start the real RailBites, so build it first
(`npm --prefix services/railbites ci && npm --prefix services/railbites run build`); a missing build fails the tests.

## Signing in (Keycloak)

Keycloak owns the passwords, sessions and tokens. The API validates its access tokens
(issuer, signature, audience `smartrail-api`, expiry) and then looks the `(issuer, subject)`
pair up in its own `identity.accounts` table. A valid token with no enabled local account gets
403; a missing or invalid token gets 401.

Settings the API reads (defaults suit the Aspire stack):

| Setting | Default | Meaning |
| --- | --- | --- |
| `Identity:Issuer` | `https://localhost:8080/realms/smartrail` | Expected `iss`; also where the signing keys are fetched. Clients and the API must all see this exact URL. |
| `Identity:Audience` | `smartrail-api` | Expected `aud` of an access token. |
| `Cors:AllowedOrigins` | `http://localhost:5173` (Development) | Browser origins allowed to call the API. |

### Local HTTPS

On its first run the AppHost creates a SmartRail development CA and a `localhost` certificate
issued by it, in `%LOCALAPPDATA%\SmartRail\dev-certs` (outside the repository). Keycloak and the
API serve that certificate; it is renewed automatically 30 days before it expires. The ASP.NET
Core development certificate is not used for them because it is not a CA, and Android refuses to
install it.

Trust the CA once on this machine (Windows asks you to confirm):

    certutil -user -addstore Root %LOCALAPPDATA%\SmartRail\dev-certs\smartrail-dev-ca.crt

`smartrail-dev-ca.pfx` in the same folder holds the CA private key: never share or commit it.
Delete the folder and remove "SmartRail Development CA" from the certificate store to start over.

### One-time local setup

1. Trust the SmartRail development CA once (see "Local HTTPS" below). Keycloak and the API both
   present certificates from it, and the API must trust the Keycloak issuer, so do not turn
   certificate validation off.
2. Start the stack: `dotnet run --project services/apphost`.
3. Prepare the database: `dotnet run --project services/api -- --migrate-and-seed` with
   `ConnectionStrings__smartrail` set to the `smartrail` database shown in the dashboard.
4. Keep the demo credentials in user-secrets (they are never committed):

       dotnet user-secrets set DemoIdentities:AdminPassword <keycloak admin password from the dashboard> --project services/api
       dotnet user-secrets set DemoIdentities:UserPassword <a password you choose for all demo users> --project services/api

5. Create the demo users and their local accounts (Development only, safe to repeat; it also
   resets their password to the secret above):

       dotnet run --project services/api -- --seed-demo-identities

   Users: `demo-passenger`, `demo-driver` (assigned to every seeded `SIM-1001` run),
   `demo-controller`, `demo-station-gph` (Gampaha only), `demo-auditor`, `demo-sysadmin`,
   `demo-simoperator`. Their emails are synthetic `@smartrail.invalid` addresses.

The seed step reads each user's subject from Keycloak and links the local account to it, and it
refuses to run if Keycloak's issuer differs from `Identity:Issuer`.

### Changing the realm

`services/apphost/realms/smartrail-realm.json` is imported only when the realm does **not**
exist. Restarting the AppHost does not update an existing `smartrail` realm. After editing the
file, either remove the Docker volume `smartrail-postgres-data` (this also clears the SmartRail
database, so repeat steps 3 and 5), or delete the `smartrail` realm in the Keycloak admin console
and restart the `keycloak` resource so it imports again.

The realm defines the public client `smartrail-ops-web` (Authorization Code with PKCE `S256`,
no secret, exact redirect `http://localhost:5173/`), an audience mapper that adds
`smartrail-api` to its access tokens only, five-minute access tokens and refresh-token rotation.

### Operations web app

    npm --prefix apps/ops-web install
    npm --prefix apps/ops-web run dev        # http://localhost:5173 (the port is fixed)

Optional overrides: `VITE_KEYCLOAK_URL`, `VITE_KEYCLOAK_REALM`, `VITE_KEYCLOAK_CLIENT_ID` and
`VITE_API_URL` (default `https://localhost:7074`, the API's HTTPS address under the AppHost).
Tokens stay in memory, so a reload does a quick Keycloak session check. **Refresh session**
forces a refresh-token exchange; **Sign out** clears the tokens and ends the Keycloak session.

### Managing accounts and grants (SysAdmin only)

These change SmartRail's own records; they never create or disable a Keycloak user. Each
change takes effect on the account's next request and writes an audit row in the same
transaction. Denied staff actions are audited too. A SysAdmin cannot change their own account.

| Request | Permission |
| --- | --- |
| `PUT /api/admin/accounts/{accountId}/status` `{ "status": "ACTIVE" \| "DISABLED" }` | `accounts.manage` |
| `GET /api/admin/accounts/{accountId}/grants` | `grants.manage` |
| `POST /api/admin/accounts/{accountId}/grants` `{ "role", "stationId"?, "validUntil"? }` (repeat to change `validUntil`) | `grants.manage` |
| `DELETE /api/admin/accounts/{accountId}/grants/{grantId}` | `grants.manage` |

`GET /api/me` shows the signed-in account's roles and effective permissions.

### Driver reports, incidents and linked service changes

Staff and driver routes for the operations workflow. Every route needs a signed-in, enabled
account, and the permission is rechecked from the database on each request, so losing a grant or an
assignment applies on the next call. Denied attempts are audited. Actors and times always come from
the server, never from the request body.

| Request | Who | Result |
| --- | --- | --- |
| `POST /api/driver/journeys/{journeyId}/reports` `{ "clientReportId", "reasonCode" }` | assigned driver (`driver.reports.create`) | `201` new, `200` an identical retry with the report's current state. Reason codes: `TRAIN_FAULT`, `SIGNAL_DELAY`, `OBSTRUCTION`, `CANNOT_CONTINUE`. A key already used for different input is a generic `409`. |
| `GET /api/driver/journeys/{journeyId}/reports[?clientReportId=&limit=&cursor=]` and `.../{reportId}` | assigned driver (`driver.reports.read`) | Only the caller's own reports; another driver's report is `404`. |
| `GET /api/operations/reports[?journeyId=&acknowledged=&limit=&cursor=]` and `.../{reportId}` | Controller (`incidents.manage`) | The report inbox, newest first. |
| `POST /api/operations/reports/{reportId}/acknowledge` (empty body) | Controller (`incidents.manage`) | `200` with the first controller's name and time; repeats and later controllers change nothing. |
| `POST /api/operations/incidents` `{ "sourceReportId" }` or `{ "reasonCode" }` | Controller (`incidents.manage`) | `201` new; `200` if the report already started an incident (even a resolved one). |
| `GET /api/operations/incidents[?state=OPEN\|RESOLVED&limit=&cursor=]` and `.../{incidentId}` | Controller or Auditor (`incidents.read`) | Includes `changeIds`, the changes the incident explains. |
| `POST /api/operations/incidents/{incidentId}/resolve` (empty body) | Controller (`incidents.manage`) | `200`, including a repeat; the first resolution time stays. |
| `GET /api/operations/incidents/{incidentId}/audit[?limit=]` | Auditor (`audit.read`) | The incident's history, oldest first. |
| `GET /api/operations/journeys/{journeyId}/audit[?limit=]` | Auditor (`audit.read`) | The journey's history, which also follows its reports and the incidents they started. |

A report is `SENT_AWAITING_CONTROL` as soon as the server stores it and `ACKNOWLEDGED` only after a
controller acknowledges it. Opening or resolving an incident never acknowledges a report or changes
a published service change; each is a separate action. "Not sent" exists only on a driver's device.

Lists page by a stable `(time, id)` order: `limit` defaults to 50 (maximum 100) and `nextCursor`, when
present, is an opaque token to send back as `cursor`.

Publishing or withdrawing a service change (`POST /api/operations/journeys/{journeyId}/changes` and
its `/preview`) accepts an optional `"incidentId"` on a **publish**. The incident must be open, and a
report-backed incident only explains changes on its report's journey; a standalone one may explain
changes on several journeys and one incident may explain many changes. Unknown incidents are `404`,
resolved or wrong-journey ones are `400`, and naming an incident also needs `incidents.manage`
(station staff cannot). The link is checked in the preview and again when confirming, and a
resolution that wins first stops the publish with a `400`. A withdrawal never takes an `incidentId`:
it keeps the link of the change it withdraws, even after the incident is resolved. Public reads
(`GET /api/journeys/{journeyId}/changes`) and Catering messages never include incident IDs.

### Passenger and driver apps (Flutter)

Both apps sign in with Authorization Code + PKCE through the system browser
(`flutter_appauth`), keep the refresh and ID tokens in secure platform storage
(`flutter_secure_storage`) and the access token in memory, and read `GET /api/me`. The realm
registers `smartrail-passenger-app` (`com.smartrail.passenger:/oauthredirect`) and
`smartrail-driver-app` (`com.smartrail.driver:/oauthredirect`) as public clients with exact
redirect URIs. The passenger app starts as a guest; cancelling sign-in or signing out returns
there. The driver app requires sign-in.

A device or emulator must reach the **same** issuer URL the API validates,
`https://localhost:8080/realms/smartrail`, and trust its certificate. Do not rewrite the issuer
or switch certificate validation off. For an Android emulator or a USB device:

1. Make the host's ports visible as `localhost` on the device:

       adb reverse tcp:8080 tcp:8080
       adb reverse tcp:7074 tcp:7074

2. Install the SmartRail development CA on the device: copy
   `%LOCALAPPDATA%\SmartRail\dev-certs\smartrail-dev-ca.crt` to it, for example

       adb push %LOCALAPPDATA%\SmartRail\dev-certs\smartrail-dev-ca.crt /sdcard/Download/

   then Settings > Security & privacy > More security > Encryption & credentials > Install a
   certificate > CA certificate. Android needs a screen lock for this. Chrome and the debug
   build of the apps then trust Keycloak and the API (`network_security_config.xml` lets debug
   builds trust user-installed CAs; release builds do not).
3. Apply the updated realm (see "Changing the realm") and run
   `--seed-demo-identities` so `demo-passenger` and `demo-driver` exist.
4. Run an app, for example `flutter run` in `apps/passenger_app`. Override the endpoints with
   `--dart-define=ISSUER=...` and `--dart-define=API_URL=...` only if the reachable URL differs,
   and then the Keycloak hostname and `Identity:Issuer` must be changed to match.

Android details that matter:

- The main activity must not set `android:taskAffinity=""` (the Flutter template adds it).
  Otherwise the sign-in redirect opens in another task and AppAuth reports "No stored state".
- Dart's own HTTP client trusts only Android's built-in CAs, so API calls use `cronet_http`,
  which checks certificates through Android and therefore trusts the development CA in debug
  builds. Its two Chromium libraries share a namespace, so `android/gradle.properties` sets
  `android.uniquePackageNames=false`.

`flutter_appauth` supports Android, iOS and macOS; it does not run on Windows or web.
