# Local foundation

## Why

We need one easy way to run the backend locally before building train features. This is a learning project: the setup should be small enough to explain and debug.

## Done when

One command starts the API, RailBites, PostgreSQL, Keycloak, and the Aspire dashboard. Both services answer a health request. We can build their images and run basic checks in CI.

## Decisions

- Keep the existing .NET 10 API and the separate Node/TypeScript RailBites service. Aspire is for **local development**, not the Azure deployment method.
- PostgreSQL has separate `smartrail` and `keycloak` databases. Keycloak runs locally with a small realm and no real users yet; login and API token validation belong to a later identity feature.
- Start with normal application logs, API request traces in the Aspire dashboard, and simple health checks. Add RailBites OpenTelemetry traces when we build its first real API-to-API flow.
- Health routes work without login. `/health/live` says the process is running. API `/health/ready` checks its database once wired. RailBites health only checks whether its small server started. A Keycloak or RailBites outage does not make the API's own health claim false.
- Use named local database storage so an ordinary restart does not erase it. No secrets or personal demo data go in Git.

## What exists now

- `services/api/Program.cs` has `/health/live`, request logs and OpenTelemetry tracing; it exports over OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- `services/railbites/` has a small HTTP server with `/health/live` (port 5200 by default, `PORT` overrides), plus build, start, dev and test scripts.
- `.github/workflows/ci.yml` builds the API and builds/tests RailBites on pull requests (not yet run on GitHub). `services/apphost` runs PostgreSQL, Keycloak (issuer `https://localhost:8080/realms/smartrail`), the API and RailBites in Aspire; see `services/README.md`. Each service has a Dockerfile (build from the repo root, see the first line of each) and CI builds both images.

## Tasks

- [x] **T1 - Make both hosts easy to run.** Add a tiny RailBites HTTP server with `/health/live` and start/test scripts; fix its CommonJS/TypeScript settings as needed. Keep the API's existing liveness route. Check: `dotnet build services/api/SmartRail.Api.csproj`, `npm --prefix services/railbites run build`, and `npm --prefix services/railbites test` pass; each host answers its health request. No Docker needed.
- [x] **T2 - Add basic API telemetry.** Configure OpenTelemetry for API requests and logs with an optional local exporter. Keep RailBites console logs clear; do not build a custom telemetry pipeline. Check: the API builds and runs without Aspire, and a request produces a useful console log. T4 checks the dashboard trace. No Docker needed.
- [x] **T3 - Add basic CI.** On pull requests, build/test the .NET API and RailBites. No Azure credentials, deployment, or container job yet. Check: the workflow commands pass locally; report hosted CI as unverified until it actually runs. No Docker needed.
- [x] **T4 - Run the local stack in Aspire.** Add an AppHost for the API, RailBites, PostgreSQL, and Keycloak. Give Keycloak its own database and a stable local issuer; wire API `/health/ready` to the SmartRail database. Check first: `docker info` reaches an engine. Then run the AppHost, open the dashboard, call both services' liveness routes and API readiness, see an API trace, and confirm database data and the issuer survive an ordinary restart. If Docker is unavailable, mark this task **Blocked** and continue any independent work.
- [x] **T5 - Build both images.** Add one simple Dockerfile per service and a root `.dockerignore`. Check: `docker build` succeeds for each, and each image answers `/health/live` when run locally. Add image builds to CI only after the local builds work. Requires Docker.

## Boundaries

This spec does not implement train data, orders, login clients, API token validation, Azure deployment, or a large monitoring stack. Use real feature work to decide when more tests or telemetry are needed. Keep start/stop instructions in a short README or runbook, not in this spec.
