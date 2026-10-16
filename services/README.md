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
07:35-08:30, `SIM-1002` 08:05-09:00, `SIM-2001` 17:10-18:30 and `SIM-9001` 23:45-00:45. It reports
nothing before it first departs and stops after it reaches its last stop; at a stop it waits at
progress 1000 until the scheduled departure. Pausing stops that journey's reports but not the clock,
so it goes ageing after three minutes and unknown after four; resuming reports where the train is
now and does not replay the missed 30-second steps. Reports already stored are never deleted. Pauses
are kept in memory, so restarting the API resumes every train.

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
