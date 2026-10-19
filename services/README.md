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
| `SMARTRAIL_BASE_URL` | none | SmartRail API address for signed callbacks and the no-show passage read. |
| `SMARTRAIL_SERVICE_KEY` | none | RailBites' own credential for that read; not the same secret as `RAILBITES_SERVICE_KEY`. |
| `RAILBITES_CALLBACK_SECRET` | none | HMAC secret shared with SmartRail's `RailBites:CallbackSecret`. Missing secret/address leaves callbacks queued. |
| `RAILBITES_CALLBACK_POLL_MS` | `2000` | Callback queue polling interval; `0` disables automatic delivery for controlled tests. |
| `RAILBITES_CALLBACK_MAX_BACKOFF_SECONDS` | `60` | Retry cap after connection failures, 408/429 or 5xx (2, 4, 8 ... seconds). |
| `RAILBITES_CALLBACK_FAILED_RETRY_SECONDS` | `600` | Slow retry after a permanent refusal, such as an incorrect signing secret. |
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
| `RailBites:CallbackSecret` | none | Must equal RailBites' `RAILBITES_CALLBACK_SECRET`; otherwise callbacks get 401. |
| `RailBites:InboundServiceKey` | none | Must equal RailBites' `SMARTRAIL_SERVICE_KEY`, for its authenticated passage read only. |
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

RailBites delivers its SQLite `webhook_outbox` to `POST /internal/catering/order-updates`, signing
the timestamp and exact body with HMAC-SHA256. SmartRail checks the five-minute timestamp window,
then commits a unique receipt and any greater status sequence together. Replays and older sequences
are acknowledged without changing the order. A callback that beats the placement response privately
looks up the provider order first, so its pickup code/items are available and a late response cannot
regress the status. If that lookup fails, no receipt is committed and RailBites retries.

For No-show, RailBites calls `GET /internal/catering/journeys/{journeyId}/stops/{stopId}/passage`
using its inbound service key. Only an observed departure proves `PASSED`; stale evidence that has
not shown departure returns `UNKNOWN`. A proven departure remains a fact when its observation ages.
Neither endpoint uses passenger tokens. Apply the new receipt migration with the usual explicit
`--migrate-and-seed` command. Keep all three service/signing secrets outside Git; AppHost address and
secret wiring and the operator failure drill remain T6.

The corridor train `SIM-3001` (Fort 09:00, Polgahawela 11:00, Peradeniya 11:50, daily) is seeded for
food ordering. The API tests start the real RailBites, so build it first
(`npm --prefix services/railbites ci && npm --prefix services/railbites run build`); a missing build fails the tests.

### Status callbacks and the passage read

RailBites tells SmartRail when an order changes (Ready, Collected, No-show, Cancelled, a new pickup time).
Each callback is a JSON body signed with HMAC-SHA256 over `<unix timestamp>.<raw body>`, sent as
`X-RailBites-Signature: v1=<hex>` with `X-RailBites-Timestamp`; SmartRail checks the raw bytes, a
five-minute timestamp window and a constant-time comparison before it reads the body. The shared
examples are in `services/railbites/fixtures/signed-callbacks.json` (regenerate with
`node services/railbites/scripts/make-callback-fixtures.js`); both services' tests run them.

| Where | Setting | Meaning |
| --- | --- | --- |
| API | `RailBites:CallbackSecret` | HMAC secret. Unset refuses every callback. |
| API | `RailBites:InboundServiceKey` | Key RailBites presents to read whether a train has passed a stop (`GET /internal/catering/journeys/{journeyId}/stops/{stopId}/passage`). Unset refuses every read. |
| RailBites | `RAILBITES_CALLBACK_SECRET` | Same secret as `RailBites:CallbackSecret`. Unset sends nothing. |
| RailBites | `SMARTRAIL_BASE_URL`, `SMARTRAIL_SERVICE_KEY` | Where to send callbacks and the passage read, and the key for it (= `RailBites:InboundServiceKey`). |
| RailBites | `RAILBITES_CALLBACK_POLL_MS` | How often queued callbacks are sent (default 2000; 0 turns the loop off). |
| RailBites | `RAILBITES_CALLBACK_MAX_BACKOFF_SECONDS`, `RAILBITES_CALLBACK_FAILED_RETRY_SECONDS` | Cap on retry delays after a timeout or 5xx (60), and the slow retry after a refusal (600). |

### Failure and recovery drill (food ordering)

A short walk through "RailBites stops, nothing is lost, it comes back" on the local stack. Start it with
`dotnet run --project services/apphost` and prepare the database as in "One-time local setup". Aspire
generates the service keys on first run and keeps them in the AppHost's user secrets; reveal them in the
dashboard under the parameters (`railbites-service-key`, `railbites-admin-key`, `kiosk-key-peradeniya`).
Below, `$RB` is the RailBites address and `$API` the API address from the dashboard, and `$TOKEN` is a
signed-in passenger's access token (sign in with `demo-passenger`).

1. **Normal.** `curl $API/health/ready` reports `Healthy`, and its `catering` entry shows
   `outboxPending: 0` and `ordersPending: 0`. `curl $RB/health/ready` is the same for RailBites.
2. **Order.** Find a journey of train `SIM-3001` dated today, make sure it has a recent sighting
   (the simulator reports it between 09:00 and 11:50, or post one as in "Simulated running"), then
   `curl $API/api/journeys/<id>/collection-options` and `POST $API/api/food/orders` with your token.
   You get `201` and a pickup code.
3. **Stop the provider.** Either stop the `railbites` resource in the dashboard, or, without stopping
   anything, switch on the Development-only outage:
   `curl -X PUT -H "X-RailBites-Service-Key: <service key>" -H "Content-Type: application/json" -d '{"active":true}' $RB/internal/dev/outage`.
   RailBites now answers `503 SIMULATED_OUTAGE` on every provider route and its `/health/ready`;
   `/health/live` and the switch itself stay reachable.
4. **Watch it degrade, not fail.** `curl $API/health/ready` is still `200` but `Degraded`, with
   `provider: Unreachable` (or `NotReady` for the switch). `curl $API/api/journeys/<id>/running` and
   `.../changes` keep working: train information does not depend on food.
5. **Make a backlog.** Place another order (you get `202` and a link to poll) and, as a controller,
   publish a delay or an early ending for the journey. The readiness `catering` entry now counts the
   waiting orders and train changes and how many seconds the oldest has waited; the Aspire dashboard
   shows the `catering.outbox.attempts{result=retry}` and `catering.placement.attempts{result=retry}`
   counters rising and RailBites unhealthy.
6. **Recover.** End the outage with `{"active":false}` (or start the resource). Within seconds the
   dispatcher delivers the change and the retry loop confirms the order under the same link ID: the
   backlog drains to zero, `catering` returns to `Healthy`, and a cancellation caused by an early ending
   reaches the passenger once, with its reason.
7. **Read the trace.** In the dashboard's Traces view open the order request: one trace runs from the
   API through RailBites and back through the signed callback (`catering.callbacks.received`). A kiosk
   action (`POST $RB/v1/kiosk/collection-points/peradeniya-junction/orders/<orderId>/ready` with
   `X-RailBites-Kiosk-Key`) starts its own trace that ends at the passenger's updated order.
8. **Change the cutoff without a SmartRail release.** `curl -X PUT -H "X-RailBites-Admin-Key: <admin key>" -H "Content-Type: application/json" -d '{"cutoffMinutes":45}' $RB/v1/admin/collection-points/peradeniya-junction/cutoff`.
   The next `collection-options` shows 45 and an earlier `orderBy`; an order or cancellation inside the
   last 45 minutes is refused with `CUTOFF_PASSED`. Orders already accepted keep their place. Neither
   service is restarted or reconfigured.

Restart either service at any point (stop it in the dashboard, or `Ctrl+C` the AppHost and start it
again): RailBites' orders, stock and queued callbacks are in `services/railbites/data/railbites.db`, and the
API's queue and receipts are in PostgreSQL, so everything resumes where it stopped.

| RailBites setting | Meaning |
| --- | --- |
| `RAILBITES_ADMIN_KEY` | Credential for RailBites' own policy changes (the cutoff). Unset refuses them. |
| `RAILBITES_DEV_CONTROLS=1` | Adds the outage switch. Ignored when `NODE_ENV=production`. The AppHost sets it; no deployed environment should. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Where RailBites sends spans and metrics (the AppHost sets it). Trace propagation works without it. |
| API `Catering:Health:BacklogDegradedSeconds` | How long a change or order may wait before readiness says `Degraded` (default 120). |

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

#### Seed journeys for the Operations T1 check

With the local Aspire database running and migrated (one-time setup above), set
`ConnectionStrings__smartrail` to its connection string, then run from the repository root:

```powershell
dotnet run --project services/api -- --seed-operations-demo
```

This command runs only in Development and exits after adding four fictional Fort–Ragama–Gampaha
journeys. It prints their service dates, workspace paths and the UTC seed anchor:

| Train | Evidence when seeded |
| --- | --- |
| `SIM-OPS-FRESH` | Current observation, two-minute reported delay |
| `SIM-OPS-AGEING` | Observation 185 seconds old; becomes unknown 55 seconds later |
| `SIM-OPS-UNKNOWN` | Observation five minutes old; schedule/history remain, position/ETA unknown |
| `SIM-OPS-OVERNIGHT` | Previous service date, 23:45 departure and 00:45 arrival; no observation |

Open the Operations corridor as `demo-controller` immediately after seeding. The fresh sample
returns unchanged ETags between polls, becomes ageing after three minutes and unknown after four.
The samples are excluded from the continuous simulator, so reading or restarting the API never
refreshes their evidence. Near midnight, their service date remains the date of departure.

Each run with a new anchor adds a new set; previous journeys, observations, changes and audit
history remain intact. To repeat the same set without duplicates or refreshing evidence, pass
the printed anchor, for example `--Seed:OperationsAnchor=2026-10-18T12:00:00Z` after the command.
Anchors must include a time-zone offset and cannot be in the future. To get fresh evidence for
a new check, omit the anchor and use the newly printed journey IDs.

For the remaining T1 browser checks, block one running request in browser developer tools
(partial failure), switch the browser offline (connection lost), then restore it (reconnect).
Seeding alone does not verify those screen states.

#### Run the app

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
