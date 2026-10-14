# Running the services locally

Requires the .NET 10 SDK, Node 24 and Docker.

## Whole stack (Aspire)

    dotnet run --project services/apphost

This starts PostgreSQL, Keycloak, the API and RailBites, and opens the Aspire dashboard
(URL and login link are printed in the console). Stop it with Ctrl+C.

- Keycloak: `https://localhost:8080` (uses the .NET dev certificate; run
  `dotnet dev-certs https --trust` once). Token issuer: `https://localhost:8080/realms/smartrail`.
  The admin password is in the dashboard under the keycloak resource.
- API: `http://localhost:5111/health/live` and `/health/ready` (ready checks the database).
- RailBites: its port is shown in the dashboard; `/health/live`.
- Database data lives in the Docker volume `smartrail-postgres-data`. Remove it to start fresh.

## One service alone

    dotnet run --project services/api                 # http://localhost:5111 with the http profile
    npm --prefix services/railbites install
    npm --prefix services/railbites run dev           # http://localhost:5200

Without the AppHost the API has no database, so `/health/ready` reports healthy with no checks.

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

### One-time local setup

1. Trust the .NET development certificate once: `dotnet dev-certs https --trust`. Keycloak and
   the API both use it; the API must trust the Keycloak issuer, so do not turn certificate
   validation off.
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
