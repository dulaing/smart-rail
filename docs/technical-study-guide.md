# SmartRail technical study guide

Prepared 21 October 2026 for an evaluation tomorrow. Start here if you can follow C# but find it difficult to explain the concepts.

This guide is grounded in the current working tree, including uncommitted changes. Code discovery used the knowledge graph, whose coverage check reported changed metadata; important behaviour was checked against source. It is a study aid, not a fresh test run, deployment verification or evidence that every acceptance task is complete. Examples labelled illustrative simplify the code.

Read alongside the [assessment brief](final-assessment-scoped%202.md), [presentation guide](evaluation-guide.md) and [backend decision](architecture/backend-decision.md). Some architecture-document wording describes an earlier starter state; the current source implements substantially more.

## 1. How to use tonight

Allow roughly **four hours**, including two ten-minute breaks. These are study estimates, not a promise of mastery.

1. **25 minutes: architecture and .NET foundations.** Sections 2–3. Explain the system aloud without reading.
2. **35 minutes: one simple request and DI.** Sections 4–5. Open the linked files and trace the station endpoint.
3. **45 minutes: publish a delay, EF and concurrency.** Sections 6–7. This is the highest-value technical walkthrough.
4. **Break: 10 minutes.**
5. **30 minutes: identity and permissions.** Section 8. Practise 401, 403 and object-level authorization.
6. **30 minutes: provider failures and freshness.** Sections 9–10. Explain a timeout, duplicate and old observation.
7. **Break: 10 minutes.**
8. **20 minutes: QA, operations and decision defence.** Sections 11–12.
9. **35 minutes: curveballs and practical drills.** Sections 13–15. Answer before reading the answer.

If you have only **90 minutes**, spend 15 on sections 2–5, 25 on section 6, 15 on section 8, 20 on section 9 and 15 answering section 13 aloud.

For every major concept, produce four things: **meaning → project example → reason → limitation**. Reading an answer is recognition; explaining it without notes is a better test of understanding.

## 2. Your architecture in ordinary language

Start with this, then explain each term:

> “SmartRail has a modular monolith backend built with ASP.NET Core 10 and feature slices. Rail, Operations, Access and Catering divide business responsibilities. They share one API deployment and one EF Core context over PostgreSQL. Grilled is a separate Node service representing the external catering company. Clients read railway state through HTTP polling. Durable provider delivery uses a database outbox.”

**Monolith:** SmartRail's backend modules run in one application process. This does not mean all files are mixed together, or that Keycloak and Grilled run inside the API.

**Modular:** code is grouped by business ownership. Rail owns timetable/running information; Operations owns staff decisions and reports; Access owns local accounts, permissions and audit; Catering owns order links and provider communication. A small Demo area provides Development-only controls.

**Feature slice:** the files for a use case sit together. The change endpoints and change confirmer are in `Operations/Changes`. You follow a feature through a few focused files instead of crossing a mandatory five-project stack.

**Boundary:** another module should use a named interface rather than manipulate the owner's tables. `ChangeConfirmer` uses `IAuditTrail`, `ICateringOutbox` and `IPickupReader`. Interfaces express what Operations needs without exposing every implementation detail.

**Actual strength of isolation:** these modules share an assembly and context. Source-based [boundary tests](../tests/SmartRail.Api.Tests/Operations/ModuleBoundaryTests.cs) catch selected violations. They are useful guardrails, but regex checks can be bypassed and do not provide hard compiler isolation.

**Data ownership:** PostgreSQL schemas are namespaces: `network`, `live`, `changes`, `identity`, `passenger`, `catering`. Six schemas do not mean six databases or six microservices. See [SmartRailDbContext](../services/api/Infrastructure/SmartRailDbContext.cs) and the [data model](architecture/data-model.md).

**Trade-off to defend:** one context makes a change, audit and delivery intent atomic. It also permits accidental coupling and gives weaker isolation than separate assemblies or contexts. Split further when independent deployment, scaling, team ownership or repeated boundary violations justify the cost.

**Do not claim:** full Clean Architecture in separate projects, MediatR, MassTransit, RabbitMQ, sagas, microservices for every module, or a full event-sourced system. Those are not the architecture selected here. Appending service-change history does not by itself make the whole application event sourced.

## 3. What .NET, C# and ASP.NET Core each do

**C#** is the language: classes, records, interfaces, generics, LINQ, async methods and pattern matching.

**.NET** supplies the runtime and libraries. The runtime executes managed code, handles garbage collection and normally uses JIT compilation to turn intermediate language into machine code. Garbage collection reclaims unreachable managed objects; it does not replace disposing a database transaction, stream or HTTP response.

**SDK** contains development tools such as the compiler and `dotnet` CLI. A runtime runs the built application; the SDK builds it. NuGet restores external packages. The API's [project file](../services/api/SmartRail.Api.csproj) targets `net10.0`; package references provide EF Core, JWT validation and telemetry integrations.

**ASP.NET Core** is the web framework: hosting, middleware, routing, model binding, authentication, authorization and HTTP responses. Kestrel is its web server; it accepts HTTP connections. Swagger UI is an interactive API explorer, and OpenAPI is the machine-readable API description. Neither makes an endpoint secure.

**EF Core** is the ORM: it maps C# objects and LINQ queries to relational data operations. **Npgsql** is the PostgreSQL provider used by EF here. PostgreSQL performs the actual queries, locks, constraints and durable transactions.

**Aspire AppHost** defines the local application's resources and wiring. It is useful for starting dependencies and viewing telemetry. It is not the component that makes `ChangeConfirmer` transactional.

### C# syntax you should be able to read aloud

From [ChangeConfirmer](../services/api/Modules/Operations/Changes/ChangeConfirmer.cs):

```csharp
public sealed class ChangeConfirmer(
    SmartRailDbContext db, IAuditTrail audit,
    ICateringOutbox outbox, IPickupReader pickups,
    ChangeStore store, TimeProvider time)
```

`public` makes the class accessible; `sealed` prevents inheritance. The parameter list is a **primary constructor**, whose dependencies DI supplies. `IAuditTrail` is an interface: it describes operations, while `AuditTrail` implements them. An interface enables substitution; it does not automatically make a design decoupled.

Other syntax in these files:

- `Task<ConfirmResult>`: an operation whose eventual result is a `ConfirmResult`. A Task is not a thread.
- `async` / `await`: allow the method to suspend while an incomplete asynchronous operation finishes.
- `Guid?`: a nullable value type. `LocalAccount?` marks a possibly null reference for compiler analysis.
- `request!`: suppresses a nullable warning. It adds no runtime null check.
- `x is { } value`: matches a non-null value and gives it a name.
- `a ?? b`: use `b` when `a` is null. `a?.B`: access `B` only when `a` is not null.
- `record`: supports data-oriented types and value-based equality. Records can still contain mutable members; they are not inherently deeply immutable.
- `result switch { ... }`: pattern matching that maps outcome types to responses.
- `[.. loaded.History, change]`: a collection expression spreading earlier items and adding the change.
- `this WebApplicationBuilder builder`: an extension method, allowing `builder.AddSmartRailAccess(...)` syntax.
- `await using`: asynchronously disposes a resource when leaving its scope, including on an exception.
- `static`: belongs to the type rather than an instance. Pure calculations can be static; stateful request services use instances.

## 4. Follow one request from HTTP to PostgreSQL

Open [Program.cs](../services/api/Program.cs), then [RailEndpoints.cs](../services/api/Modules/Rail/RailEndpoints.cs).

For `GET /api/stations`:

1. The host has registered services and built the app.
2. Middleware runs in order. Here HTTPS redirection and CORS precede authentication and authorization.
3. The Rail endpoint group has `AllowAnonymous()`, so a guest can read it despite the protected fallback policy.
4. Routing selects `GetStations`. ASP.NET supplies its `HttpContext` and registered `SmartRailDbContext`.
5. LINQ describes a read: no tracking, ordered by code, projected into public station DTOs.
6. `ToListAsync(context.RequestAborted)` executes the query through EF/Npgsql.
7. `ETagResults.JsonWithETag` serializes the response and returns JSON or 304.

**Minimal APIs** map HTTP routes directly to handlers. Controllers are another valid organization style, with classes, actions and attributes. Minimal APIs still support DI, authorization and validation; the choice does not remove those responsibilities.

**Binding versus validation:** a route GUID, body object and query value are inputs ASP.NET binds. A correctly parsed body can still violate railway rules. SmartRail explicitly validates dates, change drafts, permissions and state. Client validation improves UX; server validation protects the system.

**DTO versus entity:** a DTO is the API contract. An EF entity represents persisted data and relationships. Returning a deliberate DTO controls exposed fields and avoids coupling clients to persistence details.

**Error responses:** expected outcomes use status codes and `Results.Problem`, producing structured problem details. This does not establish that every unexpected exception has a custom production error handler.

Practise the codes: **400** invalid input; **401** missing/invalid authentication; **403** authenticated but forbidden; **404** absent resource or deliberately hidden ownership mismatch; **409** state/key conflict; **503** temporarily unavailable. A **304** means reuse your cached representation and carries no replacement JSON body.

## 5. DI, lifetimes and async: the .NET questions

### Dependency injection

> “A class states its dependencies in its constructor. The container creates and supplies them using registrations made at startup. This avoids constructing infrastructure inside business code and lets tests replace collaborators.”

Concrete registrations appear in [AccessSetup](../services/api/Modules/Access/AccessSetup.cs), [CateringSetup](../services/api/Modules/Catering/CateringSetup.cs) and [DatabaseSetup](../services/api/Infrastructure/DatabaseSetup.cs).

- **Scoped:** one instance per scope, normally an HTTP request. Appropriate for DbContext and services sharing request work.
- **Singleton:** one instance for the container's lifetime. `TimeProvider.System` is registered this way. Shared mutable state must be safe for concurrent access.
- **Transient:** a new instance on resolution. It does not mean one instance per application or necessarily one per request.

**Why not singleton DbContext?** It has mutable tracking state and is not safe for concurrent operations. Sharing it across requests risks races and unwanted shared state.

**How does a background worker use a scoped service?** Hosted workers are long-lived. [OutboxWorker](../services/api/Modules/Catering/Outbox/OutboxWorker.cs) uses `IServiceScopeFactory`; [BackgroundLoop](../services/api/Modules/Catering/BackgroundLoop.cs) creates and disposes a scope for each pass, then resolves the scoped dispatcher inside it.

Framework reference: [ASP.NET Core DI](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0).

### Async and cancellation

For database/network I/O, awaiting an incomplete operation frees the executing thread to do other work. It does not make SQL run faster, start a dedicated thread, or automatically run two statements in parallel. Sequential awaits still represent sequential work. Avoid `.Result` and `.Wait()` in request code because they block threads; ASP.NET Core can suffer thread-pool starvation even without a classic UI synchronization-context deadlock.

`RequestAborted` propagates request cancellation to cooperative operations. A worker uses the host's stopping token. Cancellation requests work to stop; it is not a distributed rollback guarantee. If a provider committed before the response was lost, reconciliation still matters.

Do not run simultaneous EF queries on the same context with `Task.WhenAll`. Separate independent work can be concurrent, but a DbContext cannot support concurrent operations.

Framework reference: [C# async scenarios](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-scenarios).

## 6. The essential walkthrough: publish a delay

Open [StaffChangeEndpoints.cs](../services/api/Modules/Operations/Changes/StaffChangeEndpoints.cs) and [ChangeConfirmer.cs](../services/api/Modules/Operations/Changes/ChangeConfirmer.cs).

### First explain the business reason

> “A driver report is evidence to review. A controller's confirmed change is an accountable operational decision. Confirmation must prevent stale edits and commit the change, its audit and the catering delivery intent together.”

Server receipt, Control acknowledgement, incident creation and change publication are separate actions. Resolving an incident does not silently withdraw its service changes.

### Then explain the code in order

1. **Preview:** load the journey and changes, check access, validate the draft and compute impact. Successful preview writes no service change.
2. **Confirm:** require the previewed version, check access again, validate and recompute impact on the server. The client cannot dictate authoritative impact or actor identity.
3. **Begin transaction.**
4. **Conditional version update:** update only the journey row whose version equals the version previewed. Move version `N` to `N+1`.
5. **Zero updated rows:** another confirmation won. Roll back and return a stale result, mapped to HTTP 409.
6. **Linked incident:** a conditional no-op update locks its open row. Incident resolution needs that row too, closing the check/commit race.
7. **Stage the service change and audit.** The server supplies actor and time.
8. **Stage catering message when relevant.** A platform-only change does not require one. Pickup times are captured in the queued message.
9. **SaveChanges, then commit.** Return confirmed state. A later background pass delivers the catering message.

### Why checking the version twice matters

The early in-memory comparison gives a clear response when already stale. It cannot settle a race: two requests may both read version 7. The database's conditional update settles it.

Illustrative SQL, with simplified names:

```sql
UPDATE journey_versions
SET version = 8
WHERE journey_id = @journeyId AND version = 7;
```

Only one competing transaction can successfully advance that row from 7. The later contender waits for the row and then no longer matches. A C# `lock` would only coordinate one process and is not the database's cross-process concurrency mechanism.

This is **optimistic concurrency** at the application level: the user previews without holding a long lock, and confirmation checks the expected version. The short database transaction still takes row locks. Optimistic concurrency does not mean “the database never locks.”

### Why an explicit transaction if SaveChanges is transactional?

`ExecuteUpdateAsync` sends its UPDATE immediately, without waiting for `SaveChanges`. The later tracked inserts must share its transaction. One SaveChanges transaction alone would not include an earlier separately executed UPDATE.

If staging audit/outbox throws after the version update, disposal of the uncommitted transaction rolls it back. No partial confirmation remains. Conversely, a response can be lost **after commit**; the client must not conclude that the operation definitely failed.

Framework reference: [EF bulk updates and transaction/concurrency behaviour](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete).

### Atomicity versus consistency across services

Inside PostgreSQL: version, change, audit and outbox intent commit together. Across SmartRail and Grilled: the provider catches up asynchronously. This is eventual consistency, and the UI/operations must represent pending delivery honestly. A local database transaction cannot roll back an already completed HTTP action at Grilled.

## 7. EF Core and the data model

### Explain these concepts using the station query

**DbContext:** a unit of work and query/change-tracking entry point for EF. **DbSet:** an entry point for querying and modifying a mapped entity type. A DbSet is not a preloaded in-memory list of the table.

**LINQ:** `.Where`, `.OrderBy` and `.Select` build the query. For EF-backed `IQueryable`, expressions are translated to SQL where supported; materializing with `ToListAsync` executes it. Once materialized, later LINQ operations run in memory. A query can also be a deliberate mixture, as the timetable endpoint loads candidates before applying domain logic.

**Projection:** selecting only the DTO fields needed reduces unnecessary data and defines the contract. **AsNoTracking:** useful for reads where EF need not remember entities for later updates. **Include/ThenInclude:** explicitly load related entities; the timetable reads load patterns and stops. This is not proof that lazy loading is enabled.

**Change tracking:** EF remembers tracked entity state. `Add` stages an insert; `SaveChangesAsync` sends tracked changes. `ExecuteUpdateAsync` bypasses tracking and executes immediately. Previously tracked objects can become stale after it; do not assume EF refreshes them automatically.

**Migration:** versioned schema evolution. The [database setup](../services/api/Infrastructure/DatabaseSetup.cs) applies migrations through explicit `--migrate-and-seed`, rather than silently at ordinary startup. This gives control over schema changes; it is not a complete production migration/backup procedure by itself.

### Domain concepts an evaluator can point at

In [TimetableTypes](../services/api/Modules/Rail/Timetable/TimetableTypes.cs):

- **Service pattern:** the repeating stopping plan and operating dates/days.
- **Scheduled journey:** one dated run of that pattern. Train number alone is insufficient to identify today's versus tomorrow's run.
- **Pattern stop:** a particular occurrence in the ordered stopping plan. A station may occur twice, so station ID alone cannot identify a stop occurrence.
- **Observation:** reported progress at a source observation time, distinct from when SmartRail received it.
- **Confirmed service change:** an authorized decision, distinct from movement evidence.

In [RailModel](../services/api/Modules/Rail/RailModel.cs), unique constraints include station code, `(PatternId, Sequence)` and `(PatternId, ServiceDate)`. Foreign keys prevent references to nonexistent related records. Check constraints restrict values such as progress to 0–1000. `Restrict` delete behaviour helps prevent accidental deletion of referenced history.

In [CateringModel](../services/api/Modules/Catering/CateringModel.cs), `(AccountId, ClientRequestKey)` is unique; outbox event IDs are unique; a webhook receipt's provider event ID is its key. The database closes races that an “exists?” query alone cannot close.

**Index trade-off:** indexes speed matching reads and can enforce uniqueness, but consume space and add write work. Use query patterns and execution plans to choose them; “index every column” is not a strategy.

**Time:** a service date uses `DateOnly`; timetable clock time uses `TimeOnly`; an instant uses `DateTimeOffset`. Day offsets preserve overnight journeys. `ColomboTime` uses a fixed +05:30 offset appropriate to this pilot. It is not a worldwide daylight-saving-time solution. PostgreSQL timestamp-with-time-zone represents an instant, not preservation of the original named zone.

## 8. Identity, permissions and security

Open [AccessSetup](../services/api/Modules/Access/AccessSetup.cs) and [CurrentAccount](../services/api/Modules/Access/CurrentAccount.cs).

> “Keycloak authenticates the user and issues tokens. SmartRail validates the access token, then checks its own enabled account, permissions and station or journey scope.”

**Authentication:** establish who the caller is. **Authorization:** decide whether that caller may perform this action on this resource.

The API validates issuer, audience, signature and expiry, with explicit access-token-type checking. A decoded JWT is not a validated JWT. A signed JWT is not necessarily encrypted: do not place secrets in readable claims. An ID token describes a sign-in to the client; the API expects an access token intended for it.

The local account is looked up by `(iss, sub)`, not a client-supplied account ID or mutable display name. `CurrentAccount` caches this lookup within a scoped request, not across all requests. Disabling an account or removing a grant applies on the next request; it does not retroactively cancel a request already in flight.

The fallback policy requires authentication and an active local account. Public railway endpoints explicitly opt out. A valid Keycloak token with no enabled local account can still be forbidden.

**Role versus permission versus scope:** a role groups permissions; a permission names an allowed action; scope constrains where or on which journey it applies. Station staff may publish platform changes for their station; a driver needs the relevant assignment. SysAdmin account administration does not automatically grant operational publishing authority.

**PKCE:** a public client creates a verifier and sends its derived challenge during authorization; the token exchange must prove the verifier. This protects authorization-code redemption. Mobile/browser apps cannot safely hide a shared client secret. See the [identity decision](architecture/identity-and-local-development.md) for the selected login flow and token storage policies.

**CORS:** a browser rule controlling which origins may read cross-origin responses. It is not an authorization system; curl and server-side callers are not stopped by CORS. Hiding a UI button is not server authorization either.

**Object-level access:** a passenger changing an order ID must not gain another passenger's order. Derive the actor from the validated principal and check ownership/assignment on the server. A GUID being difficult to guess is not access control.

**SQL injection:** normal EF LINQ values become query parameters. Raw SQL still needs correct parameterization. **XSS:** bearer tokens can be stolen or used by malicious script; memory-only token storage does not eliminate XSS risk. **CSRF:** automatically attached cookies create a different risk profile from explicitly sent bearer headers; understand the actual session mechanism before claiming blanket protection.

## 9. External integration and failure recovery

### The ownership boundary

SmartRail owns railway truth and the passenger's order link. Grilled owns menu, stock, cutoff decisions and fulfilment. The integration is HTTP, not shared-table access. The selected pilot uses payment at collection; do not invent an online payment transaction.

The typed HTTP client is registered in [CateringSetup](../services/api/Modules/Catering/CateringSetup.cs), with a default five-second timeout. `IHttpClientFactory` manages handlers/connections; it does not automatically retry arbitrary requests. [GrilledClient](../services/api/Modules/Catering/Provider/GrilledClient.cs) classifies provider responses; durable retries live in the placement/outbox workflows.

### Idempotency: one intent, safe retries

> “Repeating the same request should not create another business effect. We preserve a stable key and verify that it still describes the same request.”

In [OrderCreator](../services/api/Modules/Catering/Orders/OrderCreator.cs):

1. Compute a digest from normalized request details.
2. Look up the passenger account and client request key.
3. Same key and digest returns the existing link; different content conflicts.
4. Otherwise save a pending link before contacting Grilled.
5. The database unique constraint settles simultaneous submissions; the losing request reads the winner.
6. Reuse the link ID for provider placement and lookup.

The digest detects changed content; it does not authenticate the caller. Authentication and ownership checks do that.

In [PlacementProcessor](../services/api/Modules/Catering/Orders/PlacementProcessor.cs), timeout means **outcome unknown**, not refused. A worker retries/reconciles the durable pending link. If pickup eligibility changes, it stops creating a new order and looks up whether the earlier attempt succeeded. A settle window reduces the risk of interpreting an in-flight request as absent; its safety depends on bounded request/provider behaviour and the stable idempotency contract.

### Outbox: never lose the delivery intent

The change transaction stores an outbox row. [OutboxDispatcher](../services/api/Modules/Catering/Outbox/OutboxDispatcher.cs) selects due messages, sends their stored body and marks delivered only after provider success. Transient failures back off; refused messages are retained and retried more slowly rather than silently discarded.

**Crash after provider commit, before marking delivered:** the same event is sent again. Receiver deduplication prevents repeating its effect. Delivery is retry-based and can be repeated; do not claim exactly-once delivery or guaranteed eventual success during a permanent outage.

**Why not direct HTTP inside the change transaction?** A slow provider would hold database resources, and its remote commit cannot be undone by rolling back PostgreSQL. Persisting delivery intent decouples availability while preserving the local atomic boundary.

**Why no broker?** For one API and one provider, a database queue is sufficient and reduces operations. A broker may become useful for multiple subscribers, throughput or independent consumers, but it does not remove the need to make database changes and publication intent reliable.

### Callbacks: authentication, duplication and order are separate

Open [CallbackSignature](../services/api/Modules/Catering/Callbacks/CallbackSignature.cs), [CallbackEndpoints](../services/api/Modules/Catering/Callbacks/CallbackEndpoints.cs) and [OrderUpdateReceiver](../services/api/Modules/Catering/Callbacks/OrderUpdateReceiver.cs).

- HMAC-SHA256 signs the timestamp prefix and exact raw body bytes with a shared secret.
- Verification happens before JSON parsing; reserializing JSON could change signed bytes.
- A five-minute timestamp tolerance limits old replay; fixed-time comparison checks the signature.
- A 64 KiB body limit bounds buffering, including requests without a trustworthy Content-Length.
- Unique event IDs plus content digests detect duplicate delivery or conflicting reuse.
- A greater status sequence is required to update the order; older updates do not regress it.
- Receipt and conditional update commit together.

The callback uses `AllowAnonymous` to bypass **user-token** authentication because it has its own HMAC check. It is not an unprotected endpoint. HMAC authenticates/integrity-checks messages; HTTPS protects transport confidentiality. A valid signature does not itself establish uniqueness or newest status.

## 10. Freshness, ETags and disconnected clients

Open [RunningCalculator](../services/api/Modules/Rail/Running/RunningCalculator.cs), [FreshnessRules](../services/api/Modules/Rail/Running/RunningTypes.cs) and [ETagResults](../services/api/Infrastructure/ETagResults.cs).

Keep four facts separate: schedule = plan; observation = evidence; confirmed change = staff decision; prediction = derived estimate.

Freshness is calculated from **ObservedAt**, not ReceivedAt or the last poll. Below 180 seconds is current; 180 through under 240 is ageing; at 240 seconds it is unknown. Unknown evidence removes current position and estimates, while schedule and confirmed decisions can remain.

For usable observations, delay is the maximum of zero, reported delay, confirmed delay and how overdue the next stop is. Future served-stop estimates add that delay to scheduled times. This is a conservative rule-based pilot estimate, not a statistically trained or GPS-based predictor. Cancelled and unserved stops do not receive misleading predictions.

**ETag:** the helper hashes serialized response bytes. The client sends `If-None-Match`; matching content returns 304. The helper still computes the representation before comparison, so it primarily saves response transfer, not all database/computation work. `Cache-Control: no-cache` permits storage but requires revalidation; it does not mean “never store.”

**Why polling?** The selected 15-second web and 30-second mobile intervals fit the pilot's update rate and simplify reconnect. Expected propagation includes poll interval and processing; it is not instantaneous. SSE change hints were optional in the decision; do not present a proposed transport as a verified implementation. SignalR is justified by requirements such as groups/two-way interactions, not just because it exists.

**Offline policy to explain:** cached schedules can remain visibly old; unavailable live evidence must not look current. Saved driver material must remain owner-bound. Server writes cannot be assumed accepted offline. The precise client policies and recorded evidence are in the [presentation guide](evaluation-guide.md) and app specs; backend/API proof is not a substitute for a device demonstration.

## 11. Tests, observability and operational limits

**Unit tests:** exercise pure rules with controlled inputs, such as freshness, change folding and running estimates. Passing time in, or injecting `TimeProvider`, avoids slow sleeps and boundary flakiness.

**Integration tests:** exercise HTTP, persistence, permissions and transaction behaviour together. [TimetableApi](../tests/SmartRail.Api.Tests/Rail/TimetableApi.cs) uses `WebApplicationFactory<Program>` and a disposable PostgreSQL database. The public partial `Program` declaration enables the test host to reference the entry-point type.

**Why real PostgreSQL?** An in-memory fake cannot establish PostgreSQL locking, SQL constraints, transactional rollback or provider translation behaviour. The database tests are skipped when `SMARTRAIL_TEST_POSTGRES` is not configured; an apparently green test command can therefore omit crucial checks.

**Contract/system tests:** provider communication and shared signed fixtures check cross-service behaviour. **End-to-end UI tests:** verify the user journey across real screens; an API acceptance test does not prove Flutter navigation, browser accessibility or native sign-in.

Point to [PublishApiTests](../tests/SmartRail.Api.Tests/Operations/PublishApiTests.cs): tests cover atomic change/audit/message, no message for platform-only change, authorization, stale versions, concurrent confirmation and rollback on injected audit/outbox failures. This guide inspected tests; it did not run them.

The [CI workflow](../.github/workflows/ci.yml) builds/tests the API and provider with PostgreSQL and builds AppHost and containers. The [pilot acceptance spec](../_specs/pilot-app-acceptance.md) still leaves T2–T4 unchecked. Establish what actually ran before claiming all five assessment E2E flows passed.

**Logs:** individual events and diagnostic detail. **Metrics:** aggregate rates, counts and backlog age. **Traces:** a request's path across API and provider calls. **Audit:** accountable domain history of actor/action/target/time; ordinary diagnostic logs do not replace it.

[Program.cs](../services/api/Program.cs) configures OpenTelemetry instrumentation and optional OTLP export. Structured log placeholders preserve fields for search. Avoid logging access tokens, secrets, full callback bodies or pickup codes.

**Liveness:** process responds; `/health/live` runs no dependency checks. **Readiness:** ability to serve relevant work, including dependency checks. Catering is allowed to be Degraded while train reads remain available; the configured response still returns 200 for Degraded. Health status must be interpreted with its dependency details. Without a DB connection string, DB-backed routes and that DB check are not registered; a green process alone is weak evidence of readiness.

**Production improvements to discuss as future work:** verified restore/backups, reviewed migrations, secret rotation, rate limiting, retention policy, alert thresholds and multi-instance worker coordination. Do not describe a local Aspire dashboard or successful container build as proof these are complete.

## 12. Defend decisions without pretending they have no cost

Use: **constraint → choice → benefit → cost → revisit trigger**. Speak only for decisions you actually own.

**Why a modular monolith?** “One engineer and a small corridor benefit from one deployment and local transactions. Modules keep ownership readable. The cost is shared deployment/scaling and weaker isolation. I would split for demonstrated independent ownership, release or scaling needs.”

**Why EF rather than hand-written SQL everywhere?** “Mappings, queries and migrations reduce repetitive persistence code. For critical races I still use an explicit conditional database update. EF does not remove the need to understand SQL, tracking and transactions. I would profile real queries before optimizing hotspots.”

**Why no generic repository/MediatR for every action?** “The feature slice is small and direct. Extra wrappers would add navigation without automatically improving correctness. Named interfaces exist where there is a real module or external boundary. I can introduce more structure when actual complexity warrants it.”

**Why PostgreSQL?** “The core records are relational and need constraints and transactions. PostgreSQL fits atomic railway decisions and durable delivery queues. It adds an operational database dependency, so backup, restore and schema management matter.”

**Why Keycloak?** “Credential/session/token handling is delegated to an identity system; railway permissions remain local. The cost is operating an additional dependency and configuring issuer, clients and certificates correctly. Identity availability and deployment need explicit attention.”

**Why Grilled separate?** “The assessment requires an external provider, and menu/stock/fulfilment have different ownership. Separation forces an explicit contract and failure policy. It costs us retries, idempotency, observability and eventual consistency.”

**Why React and Flutter?** “Browser staff/board experiences and native passenger/driver workflows have different deployment and interaction needs. Flutter supports shared mobile implementation; React fits the web surfaces. The cost is multiple stacks and accessibility/platform testing.”

**Why less framework machinery?** “I selected the smallest implementation that supports the requirements I can explain and verify. I did not copy the installed five-project reference architecture. Framework count is not evidence of maintainability.”

Do not invent a personal rationale such as “I benchmarked these alternatives” unless you did. A sound explanation can say the choice was based on constraints and existing skill, with no benchmark claim.

## 13. Curveball questions: answer first, then check

1. **Two requests both pass the version check; why isn't that a bug?** The early check is advisory. The conditional database UPDATE decides the winner inside the transaction; the loser gets 409.

2. **Why not automatically retry the stale controller change?** It could apply intent the controller never reviewed against the new state. Require a new preview and confirmation.

3. **Does confirming a delay need proof the user opened the preview screen?** The server requires an expected version and recomputes the impact. That is state/concurrency protection, not cryptographic proof of a human screen interaction or a stored preview token.

4. **What if the incident is resolved between validation and commit?** The confirmer and resolver coordinate on the incident row. The winner determines whether a new linked publish can proceed.

5. **Is the entire system strongly consistent?** The local confirmation is atomic. Provider delivery and client polling are asynchronous, so those views converge later.

6. **Could the outbox send twice?** Yes, especially after provider commit and lost acknowledgement. Stable event identity and receiver deduplication are required.

7. **Can you run two API replicas tomorrow?** First review worker claiming/leases, concurrent dispatch, ordering, simulator coordination and shared configuration. The shown dispatcher does not itself claim work with a lease or `SKIP LOCKED`. Database version checks still work across processes, but that does not prove every worker scales safely.

8. **Why doesn't an HTTP timeout mean “order failed”?** It only means the caller stopped waiting. Grilled may have committed. Preserve the key/link and reconcile.

9. **Why is the pre-insert “exists?” lookup insufficient?** Two requests can both see nothing. A unique database constraint resolves their insert race.

10. **Why use both event ID and status sequence?** ID recognizes the same message. Sequence orders different messages about the same order. A valid newer delivery can contain an older business update.

11. **Can a signed callback be replayed?** A captured valid message can be resent within the time window. Deduplication prevents repeat effect; timestamp validation alone is insufficient.

12. **Why not parse and reserialize before checking the signature?** Whitespace/property order/encoding may change; the signature covers exact bytes.

13. **If the token is valid, why query PostgreSQL for permissions?** Local disabling, grants and assignments must take effect on the next request without waiting for token expiry. This adds database reads and availability coupling.

14. **Does removing a grant invalidate the JWT?** No. It changes local authorization. Token validity and current permission are separate questions.

15. **Can SysAdmin publish a delay?** Only with the needed operational grant; account administration is not blanket railway authority.

16. **The poll returned 304. Can I say the train is fresh?** No. That says the response representation is unchanged. Evidence age still derives from observation time.

17. **No new observations arrived. Can the response ETag change?** Yes. Crossing freshness thresholds or changing a time-dependent prediction changes the derived representation.

18. **Why are estimates null rather than copied from the timetable?** A plan is not evidence of current running. Null preserves the distinction when evidence is unknown.

19. **Does a confirmed delay refresh a train location?** No. An authorized decision is not a new observation.

20. **What happens to orders if a change is withdrawn?** New eligibility may change, but an already cancelled provider order must not silently resurrect. Railway state and order lifecycle are separate.

21. **Is this CQRS?** Reads and writes are separated in places, but there is no need to claim a full CQRS infrastructure or separate read/write databases. CQRS is about distinct command/query models and responsibilities; MediatR is only one possible tool.

22. **Is audit immutable?** It is application-managed history. Do not claim cryptographic tamper evidence or protection from a database administrator unless separately implemented and verified.

23. **Where are your safety-critical train controls?** This is a passenger-information/operations pilot with simulated progress. It is not signalling, braking or certified train-control software. Explain the actual operational boundary.

24. **What if Keycloak is down?** Existing access-token verification may continue while cached signing keys and token lifetime permit; login, refresh or fetching missing signing keys can fail. Do not promise unrestricted offline authentication.

25. **What if I ask you to change a requirement now?** Identify the owning rule and contract, update meaningful tests, then check affected clients. Avoid changing only a label while leaving server behaviour inconsistent.

## 14. Practical evaluator drills

These are rehearsal exercises, not implementation requests. Explain the patch and check you would make before editing.

**Change unknown-after from four minutes to five.** Locate `FreshnessRules.UnknownAfterSeconds`, boundary tests/shared vectors and threshold-bearing responses. Review client use of server thresholds and cached policies. Check 299 seconds versus exactly 300, plus no observation. Do not change only UI text.

**An order appears twice after retry.** Trace client request-key persistence → account/key unique constraint → request digest → stable provider link ID → provider deduplication. Inspect whether a retry accidentally generated a new intent. A timeout log alone cannot tell you which side committed.

**A station employee changed another station's platform.** Trace token → enabled local account → permission → station derived from targeted stop → scoped grant. Test the forbidden target directly through HTTP; hiding it in a dropdown is insufficient.

**Publishing succeeds but food timing never changes.** Establish the journey/change ID and version; inspect stored outbox event, state, attempt count, next retry and error; follow the trace into Grilled. Check correct journey/stop, provider contract and callback backlog. Repeatedly publishing a new delay to “unstick it” can obscure the defect.

**After a replica restart, pending work vanished.** Check that it was committed to durable PostgreSQL/SQLite rather than only held in memory, and that the restarted process points at the same durable storage. A background Task alone is not a durable queue.

**Tests pass locally but CI finds a database failure.** Check skipped tests and the test connection-string variable, PostgreSQL version, migrations and provider build prerequisites. Compare actual executed test counts and failure output.

**A train is shown as current with a stale report.** Compare source observation time, receive time, server time and display age. A recently received old observation must not reset freshness. Check exact threshold comparisons and any client clock assumptions.

## 15. Self-test before sleeping

Close this guide and explain these without jargon you cannot unpack:

1. The architecture and one deliberate trade-off in 60 seconds.
2. `GET /api/stations`, including DI, EF execution and ETag, in 90 seconds.
3. Publish a delay, including the actual concurrency winner, in three minutes.
4. Authentication versus authorization with a station-scoped example.
5. The order whose provider response disappears.
6. A duplicate signed callback arriving after a newer one.
7. Why polling can succeed while current train position remains unknown.
8. The exact tests/evidence you have, and the checks still incomplete.

For AI ownership, bring one real example of a suggestion you changed or rejected and the evidence you used. The repository supports explaining the simpler feature-slice decision, but it does not establish your personal involvement in every decision. Do not attribute a defect to AI without evidence.

When you do not know: “I haven't verified that. What I can show in this implementation is ___. To establish the stronger claim, I would check ___.” A precise limit is more defensible than inventing a guarantee.
