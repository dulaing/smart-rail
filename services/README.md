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
