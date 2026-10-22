# Backend decision — SmartRail pilot

Status: **accepted for the pilot**, 13 October 2026; local development and identity decisions revised 14 October. This explains and sharpens [scope decisions D1, D6, D7, D9, D11, D12 and D14](../06-scope-decision.md). The API is still a .NET 10 starter; this document does not claim the modules, identity or live updates have been implemented.

## The decision in one sentence

Build one ASP.NET Core 10 API as a **modular monolith with feature slices**; use PostgreSQL as the durable source of truth and ordinary HTTP polling with ETags as the guaranteed update path. Add a small, one-way SSE *change hint* only after the five core journeys work. Grilled remains a separate Node service because its integration boundary is part of the assessment.

## Why this architecture

| Option | Decision for 20 October | Reason |
| --- | --- | --- |
| One flat API | Reject | Easy initially, but ownership of schedule, changes, accounts and food becomes unclear. |
| Clean Architecture repeated in every module | Reject | The [reference blueprint](https://github.com/ashankasr/modular-monolith-eventdriven) uses five projects per module plus MediatR, MassTransit, RabbitMQ and sagas. Those are valid teaching examples but add more files and failure modes than this one-engineer pilot needs. |
| Feature slices inside a modular monolith | **Choose** | One deployment and database, explicit module boundaries, and each use case can be read from endpoint to rule to persistence without jumping across layers. |
| Separate SmartRail microservices | Reject | Requires distributed coordination for a service change, audit and catering handoff, while the pilot has one engineer and one week left. |

Clean Architecture's useful principle is **direction of dependency**: business rules should not depend on HTTP, EF or Azure. It does not require four or five projects for every module. Keep complex change, freshness, ETA and order-eligibility rules in plain C# classes; keep simple reads as direct EF queries. Microsoft likewise recommends matching internal patterns to the complexity of each area rather than applying one pattern everywhere: [archite cture guidance](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/multi-container-microservice-net-applications/microservice-application-design).

## Code boundaries

Keep **one API project** for the pilot. Under `services/api/Modules/`, use four named areas:

| Module | Owns | Does not own |
| --- | --- | --- |
| `Rail` | Published/fictional patterns, stations, dated journeys, simulated observations, freshness and ETA calculation | Staff decisions |
| `Operations` | Driver reports, incidents, change preview/publish/withdrawal and version conflict | Train timetable source |
| `Access` | Accounts, roles, assignments, follows, saved routes and audit | Train or provider order state |
| `Catering` | SmartRail order links, Grilled HTTP contract, outbox and webhook receipts | Grilled menu, stock or orders |

The PostgreSQL schemas in the [data model](data-model.md) remain `network`, `live`, `changes`, `identity`, `passenger` and `catering`; a module may own more than one schema. A schema is a database namespace, **not** another service.

The Access module owns SmartRail permissions and local account references; **Keycloak** owns authentication, credentials and refresh tokens. The API validates Keycloak tokens before applying local role, station and journey rules. Keycloak has its own database and is not another SmartRail module. The [identity and local development decision](identity-and-local-development.md) gives the login and environment contracts.

Each feature folder contains only the files its use case needs: endpoint/request and response, business rule or handler, persistence mapping, and a focused test. Avoid generic repositories, a MediatR handler for every query, a shared `Utilities` folder, and duplicate DTO/entity layers. Begin with one EF Core context so a change, audit row and Grilled outbox row can commit in **one PostgreSQL transaction**. Place entity mappings beside the owning module and map them to its schema. Cross-module use goes through a small, named interface; feature code must not query another module's EF sets directly. Add an architecture test when the second module exists to enforce that boundary. One context is an explicit pilot trade-off: simpler transactions, weaker compile-time isolation than separate assemblies.

The HTTP host (`Program.cs`) only configures services, middleware and module endpoints. It must not contain train logic. A path through the first slice should be easy to explain: `GET /api/stations` → Rail endpoint → Rail query → EF mapping → `network.stations` → response. For a published change: Operations endpoint → version check → one database transaction for change + audit + provider outbox → commit → client change hint. The outbox worker later retries Grilled; the hint is never the durable record.

## Updates to clients

| Transport | Fit here | Decision |
| --- | --- | --- |
| HTTP polling + ETag/304 | Simple across React and Flutter; survives restart and reconnect; 15 seconds on station/ops screens and 30 seconds on mobile | **Required baseline** |
| Server-Sent Events | One-way server-to-client hint over HTTP; useful for an open screen to refetch earlier | Optional after core; no personal data in the public stream |
| SignalR | Connection management, groups and two-way hub calls; can itself fall back to SSE | Defer until a real two-way or high-frequency need appears |
| Raw WebSocket | Two-way transport but requires our own protocol, reconnection and fan-out | Reject for pilot |
| Mobile push | Alerts when an app is closed, not a replacement for in-app state sync | Outside pilot |

The train position is **simulated every 30 seconds**, and most interactions are reads. Driver reports and controller changes are normal HTTP writes; they do not need a duplex socket. SignalR is not automatically more efficient or less efficient than SSE in every setting: it solves a broader problem and can use SSE as one of its transports ([Microsoft SignalR overview](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction?view=aspnetcore-10.0)). ASP.NET Core 10 provides an SSE response API if the optional hint earns its place ([Microsoft SSE docs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/responses?view=aspnetcore-10.0#server-sent-events-sse)).

**Contract:** REST returns the full, authoritative state with source and timestamps. An SSE message, if added, contains only a resource ID and version meaning “fetch again.” On open, reconnect or a missed hint, the client fetches REST. ETag/304 avoids resending unchanged state. A connected stream never makes a four-minute-old observation fresh. The passenger app's offline cache is local to the device. A single Azure API replica avoids a distributed fan-out mechanism for the pilot; scale-out would need a shared notifier or managed real-time service and a new decision.

## Other decisions that protect the demo

- Public schedule provenance and simulated running data remain visibly distinct. No real-time railway API is assumed.
- `409` on a stale whole-journey preview and order idempotency are database rules, not socket features.
- A confirmed change, audit row and Grilled outbox record must be atomic. Grilled can be down without losing the change.
- Health checks, structured logs with correlation IDs, outbox backlog and feed age matter more than transport choice. Use OpenTelemetry for both environments: the Aspire dashboard locally and Application Insights in Azure. Aspire AppHost runs local dependencies and the demo fallback. Use one Azure environment and budget alerts as planned in D11; include the separate Keycloak service and database in the new estimate.
- Product AI remains outside this pilot's operational decisions. If added later, a human must approve any AI-written notice and all train facts must come from structured data (D14).

## Reference skills and limits

The eight `.claude/skills` from [the blueprint](https://github.com/ashankasr/modular-monolith-eventdriven/tree/main/.claude/skills) were installed into the user's Codex skills directory from commit `07ae4c1c2a7b80333f24e9afd4db61043ca9d583`. They cover architecture, module creation, feature implementation, domain/EF, CQRS, integration events, sagas and endpoints. They are **reference material**, not SmartRail's architecture contract. In particular, the five-project module generator, RabbitMQ, MassTransit, saga and automatic startup migrations are not selected by this decision.

## First implementation slice

1. Replace the starter weather endpoint with host wiring and `/health/live`.
2. Implement Rail's stations, timetable patterns/stops and dated journeys in PostgreSQL, including provenance and unique constraints.
3. Seed a small, repeatable corridor timetable and expose guest `GET` endpoints with ETags.
4. Add simulated observations and the four-minute unknown rule, with deterministic tests.
5. Then add Operations' versioned change transaction. Only after that, wire outbox, accounts and catering.

**Revisit:** switch to SignalR only if a demonstrated use case needs two-way hub calls or groups; split modules into assemblies if boundary violations become frequent; add a broker only if one-process delivery and the database outbox no longer meet observed needs.
