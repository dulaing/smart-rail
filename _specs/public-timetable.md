# Public timetable

## Why

A passenger should be able to find stations and the next scheduled train without creating an account. This gives the pilot its first useful railway API before live updates and personal features.

## Done when

Anonymous requests list stations, inspect journeys for a service date, and return up to three direct scheduled trains between two stations. Every journey has an honest source label; an empty route returns an empty list, and malformed requests return a clear error.

## Decisions

- Follow the [backend decision](../docs/architecture/backend-decision.md#code-boundaries): keep Rail features under `services/api/Modules/Rail/` in the existing API, use one EF Core context, and keep `Program.cs` to wiring. No extra business projects or generic repository layer.
- Use the [data model](../docs/architecture/data-model.md#tables-and-rules-not-fully-visible-in-the-diagrams) and [timetable ERD](../docs/architecture/erd/01-timetable-running.mmd): stations, service patterns, ordered pattern stops, and dated journeys in the `network` schema. Preserve the documented unique keys, operating days/effective dates, and repeatable seed identity; do not add a `journey_stops` table.
- Convert local stop times plus `day_offset` and the Colombo service date into scheduled instants. An early-morning request can match an overnight journey from the previous service date. Match stop occurrences by sequence, not just station ID; choose the earliest eligible origin occurrence with a later destination and return each journey once.
- Return provenance with each journey: `sourceKind`, `sourceLabel`, `sourceUrl`, and `sourceCheckedAt`. `PUBLISHED` requires a checked source and says "Published timetable"; `FICTIONAL_DEMO` says "SmartRail simulation". The [pilot examples](../docs/pilot-services.md) still need date-specific verification: use fictional demo seeds until a published service is hand-checked. A timetable source is not a live train feed.
- Public reads stay anonymous under the [identity boundary](../docs/architecture/identity-and-local-development.md#identity-boundary); no Keycloak token or running sign-in service is required.
- Proposed API: `GET /api/stations`, `GET /api/journeys?serviceDate=YYYY-MM-DD`, and `GET /api/next-trains?originId=...&destinationId=...&at=...`. `at` is an optional offset-bearing ISO timestamp, defaulting to server time. Next trains depart at or after that instant, serve the destination later, and sort by origin departure with a stable tie-breaker. Unknown stations return 404; invalid dates/timestamps or identical stations return 400; a valid route with no matches returns 200 with an empty list.
- Include scheduled origin arrival/departure, destination arrival, planned boarding platform, journey/stop identifiers, and source metadata. Label the result "Scheduled times only — not live"; do not invent an ETA or recommend the fastest arrival. Departure-based eligibility reflects Kasun's passenger advice. Add ETag/304 for unchanged read results as required by the [update contract](../docs/architecture/backend-decision.md#updates-to-clients).

## What exists now

- `services/api/Modules/Rail/` holds the timetable types, matching rules (`Timetable/NextTrainFinder.cs`), EF mapping (`RailModel.cs`), demo seeder, and the three anonymous routes. `Infrastructure/` holds the one EF context, the `--migrate-and-seed` command, and the ETag helper. The initial migration is in `Infrastructure/Migrations/`.
- The routes are mapped only when `ConnectionStrings:smartrail` is set. Run `dotnet run --project services/api -- --migrate-and-seed` once to create the `network` schema and seed fictional journeys (`Seed:StartDate`, `Seed:Days` configure the dates). The seed is fictional only; no published service has been hand-checked yet.
- Not yet done: the AppHost run of this feature (local foundation T4 is separate work), and hosted CI for the new test step and PostgreSQL service.

## Tasks

- [x] **T1 - Prove dated route matching with small fixtures.** Add the Rail timetable types and plain time/stop matching rules under `services/api/Modules/Rail/Timetable/`, with focused tests in `tests/SmartRail.Api.Tests/` (one API test project if needed). Check: `dotnet test tests/SmartRail.Api.Tests` covers weekday/effective-date filtering, Colombo midnight/day offsets, reverse routes, repeated stations, passed arrivals with future departures, the exact departure cutoff, and one result per journey. No database required.
- [x] **T2 - Read stations from a repeatable database seed.** Needs a reachable `ConnectionStrings:smartrail` PostgreSQL database (the API tests read `SMARTRAIL_TEST_POSTGRES`; the checks ran against a disposable `postgres:18.3` container). Add the EF/Npgsql context, `network` mappings/migration, a small configurable-date fictional seed, and anonymous `/api/stations`. Files: `services/api/Modules/Rail/`, `services/api/Infrastructure/`, API configuration/project, and API tests. Check: on a disposable PostgreSQL database, migrate and seed twice, then an unauthenticated station request returns the same unique station IDs/codes and stored names.
- [x] **T3 - Inspect dated journeys and their sources.** Depends on T2. Add anonymous `/api/journeys` returning ordered stops, scheduled instants/platforms, and source metadata. Files: the Rail feature and API tests. Check: endpoint tests on the seeded database show different service dates correctly, reject invalid dates/published provenance, preserve the simulation label, and return an empty list for a date with no journeys.
- [x] **T4 - Ask for next trains without login.** Depends on T3. Add `/api/next-trains` using T1's rules and ETags on the public reads. Files: the Rail feature, `services/api/Program.cs` wiring, and API tests. Check: an anonymous seeded Gampaha-to-Fort request returns at most three correctly ordered eligible journeys; tests cover 400/404/empty results, overnight matching, 304 for unchanged content, and a new ETag when represented timetable data changes.

## Boundaries

This feature delivers backend timetable reads, not passenger UI, live observations/ETAs, cancellations/platform changes, account mapping, follows, food orders, or automatic timetable import. Keep scheduled times and source-check timestamps separate from future live-update timestamps. Never describe a fictional seed or an unverified published service as an actual train running today.
