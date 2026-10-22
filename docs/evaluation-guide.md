# SmartRail evaluation — speaking guide

Prepared 20 October 2026 from the assessment brief, current source and recorded evidence. This is a presentation plan, not a fresh live-stack verification. Existing uncommitted work was preserved.

## 1. Your story in one sentence

**“A railway change should be confirmed once, reach everyone affected, and never make old information look current.”**

Present a journey through the system. Open the code only after showing the value.

Suggested length: **15 minutes**, then questions. For 10 minutes, shorten FigJam and show only one failure case.

## 2. Opening — 45 seconds

Say:

> “Imagine a train is delayed. The driver knows, but the passenger, station board and food kiosk may still show the original plan. SmartRail connects those people around one confirmed update. I built a corridor-scale pilot with passenger and driver apps, an Operations website, station displays and a separate catering provider. Train movement is simulated and clearly labelled.”

## 3. FigJam — 2 minutes

Walk through **problem → people → missing rules → scope → design**.

- **People:** passenger needs a travel decision; driver needs a briefing; Control needs reviewed changes; station needs glanceable information; caterer needs collection timing.
- **Missing rules:** What if an update stops? Does receiving a report mean Control acknowledged it? What if an order succeeds but its response disappears? Who can change a platform?
- **Decisions:** unknown after four minutes; explicit acknowledgement; same-key retries; permissions with station/journey scope.
- **Scope:** one corridor and connected flows. Defer real tracking, ticketing, online payment, background push and the interactive terminal.
- **UX:** guest-first train browsing; schedule separate from estimate; source/time visible; words and icons alongside colour; three languages together on public boards.

Say:

> “I reduced scope around complete journeys. A delay reaching the passenger and caterer mattered more than adding another disconnected feature.”

Transition:

> “Now I’ll follow that decision through the working applications.”

## 4. Live demo — about 8 minutes

### Before presenting: choose the actual trains

Use **one dated journey ID** across passenger, driver, Control and board for the main change. Match the board station to a stop on that journey.

- The seeded driver is assigned to **SIM-1001**, not automatically SIM-3001.
- Guest browsing opens **Gampaha → Colombo Fort**.
- Food uses the separate outbound **SIM-3001: Fort → Polgahawela → Peradeniya**. Say that you are switching journeys.
- A train number repeats; a dated journey identifies the particular run.
- Changes persist. Check existing changes, orders and versions before rehearsal. Do not assume a clean database.

### A. Passenger: establish the starting point — 45 seconds

**Do:** Open passenger Home as a guest → route/next trains → train detail.

**Point at:** scheduled time, estimate if available, platform, update age, simulation attribution and route diagram.

**Say:**

> “Passengers can check trains without an account. The timetable is the plan; the estimate depends on recent evidence. This is simulated progress between stations, not GPS.”

Do not call a missing estimate a defect when the observation is unknown.

### B. Driver: report the problem — 45 seconds

**Do:** Open the assigned journey → Reports → choose a non-urgent reason such as signal delay → submit once.

**Point at:** server receipt / awaiting Control acknowledgement.

**Say:**

> “The server has received this report. Control has not acknowledged it yet. Those are separate states. Urgent situations still require radio.”

If the receipt is uncertain, say that. Use **Retry same report**, not a new report.

### C. Control: turn the report into an accountable decision — 90 seconds

**Do:** Operations `/reports` → open the report → explicitly acknowledge → open an incident from it. Then Corridor → the same journey → compose a **25-minute delay** → link the open incident → preview → **Confirm and publish**.

**Point at:** acknowledgement name/time, previewed impact and journey version.

**Say:**

> “A driver report does not automatically change public information. An authorised controller reviews and confirms the change. The preview shows what will be affected before publishing.”

Opening a report is not acknowledgement. Opening an incident is not publication.

### D. Follow the same change — 60 seconds

**Do:** Return to passenger detail → matching station board → driver briefing. Refresh if needed; normal propagation uses polling.

**Point at:** the same confirmed delay and confirmation time. Keep schedule and estimate distinct.

**Say:**

> “These screens read the same confirmed decision. The public sees ‘Staff-confirmed change’ and its time. The protected driver briefing can also show the staff name.”

Station boards have entrance and platform views. Explain one; briefly show the other. A platform move can leave a ‘moved from X to Y’ notice at the original platform.

If evidence is unknown, the confirmed delay can remain visible while the ETA stays unavailable. Publishing a delay does not refresh the position.

### E. Food: cross the external-system boundary — 90 seconds

**Say first:**

> “I’m switching to an outbound journey. This passenger is travelling through a food collection station.”

**Do:** Food → today's SIM-3001 → an eligible collection point → menu → basket/review → sign in when requested → place order → show pickup code. Publish a reviewed delay on **this outbound journey** → refresh its order → show changed pickup time.

**Say:**

> “SmartRail owns the railway information and the passenger’s order link. Grilled owns menus, stock, cutoff rules and fulfilment. Payment happens at collection.”

**If rehearsed:** Grilled `/staff` → point-scoped login → Ready → enter the passenger's code → Collected → passenger order updates.

> “The staff page does not reveal the code it is meant to check. Grilled sends the result back through a signed callback.”

Use a legitimately pre-placed order for the collection segment if needed. Do not wait for a long trip to finish on stage. Check the real provider's transition rules during rehearsal.

**Optional alternative:** end the outbound train at **Polgahawela**. Peradeniya is later, so an open Peradeniya order is cancelled; earlier served stops remain. Withdrawal permits new eligible orders but does not revive cancelled orders.

### F. Show one failure, then accountability — 90 seconds

**Choose one failure already rehearsed:**

1. **Old evidence:** show an already-unknown simulated train. No current marker or ETA. Explain current <3 minutes, ageing at 3, unknown at 4. Resume only through an available authorised API/control, then show new evidence if the simulator is actually in service.
2. **Board offline:** use a production build whose service worker is already controlling the page. Disconnect that browser → reload → timetable-only board and one unavailable-updates banner → reconnect.
3. **Provider outage:** use the existing Development outage control → food is pending and delivery backlog rises, train reads still work → restore → observe recovery.

**Say:**

> “Losing evidence is not evidence that a train is on time. Losing the food provider should not remove train information.”

**Finish:** resolve the incident in Operations → show that the delay is still effective → open Auditor history in a separate session.

> “Resolving the incident records that the problem is resolved. Removing the service change is a separate reviewed action. The history records who did what and when.”

Do not wait four minutes doing nothing. Prepare an unknown train or start the pause before the talk. The Operations `/demo-controls` page currently remains a placeholder; use a rehearsed authorised API or Aspire, and label it as such.

## 5. Explain the architecture — 2 minutes

Say:

> “I chose a modular monolith: one backend deployment, separated by business responsibility. It gives this pilot simple deployment and database transactions, while keeping boundaries clear. Grilled stays separate because it represents an external company.”

Four main business modules under `services/api/Modules/`:

| Module | Owns |
| --- | --- |
| Rail | Stations, timetable patterns, dated journeys, observations and running calculations |
| Operations | Reports, acknowledgement, incidents, reviewed changes and edit conflicts |
| Access | Local accounts, permissions, station/journey scope, personal preferences and audit |
| Catering | Passenger order links, provider HTTP integration, delivery outbox and callback receipts |

Demo controls are a small additional Development-only area.

- **Database:** one SmartRail PostgreSQL database, six schemas, one EF Core `SmartRailDbContext`. Keycloak has its own database; Grilled has SQLite.
- **Boundaries:** cross-module work uses named interfaces such as `IAuditTrail`, `ICateringOutbox` and `IPickupReader`. Source-based tests guard selected module boundaries. These are not separate assemblies or hard service isolation.
- **Feature slices:** endpoint → focused rule/service → persistence. Simple reads use EF directly; complex rules are plain testable C#.
- **Trade-off:** shared context makes atomic writes easy; separate assemblies/contexts would provide stronger isolation and more work.
- **Deliberate exclusions:** no MediatR, MassTransit, RabbitMQ, saga or five-project module template in this implementation.

### The most useful technical walkthrough

Open `services/api/Modules/Operations/Changes/ChangeConfirmer.cs`.

Say:

> “This is the point where a preview becomes a real change. It checks the journey version, then saves the change, audit entry and any food-provider message in one transaction. Either all commit, or none do.”

Follow the file: begin transaction → conditional version update → stage change/audit/outbox → save → commit.

The HTTP request does not wait for Grilled to consume the change. A background worker delivers it later. Platform-only changes do not enqueue an irrelevant catering message.

## 6. Dependency injection — 30 seconds

Say:

> “Dependency injection means a class declares what it needs, and .NET supplies it. The class does not build its own database, clock or provider connection. That keeps responsibilities clear and lets tests replace dependencies.”

Real example from `OperationsSetup.cs`:

```csharp
builder.Services.AddScoped<ChangeConfirmer>();
builder.Services.AddScoped<IAuditTrail, AuditTrail>(); // registered by AccessSetup
```

`ChangeConfirmer` requests `SmartRailDbContext`, `IAuditTrail`, `ICateringOutbox`, `IPickupReader`, `ChangeStore` and `TimeProvider` in its constructor.

- **Scoped:** one instance per request/scope; appropriate for the EF context and transaction-related services.
- **Singleton:** one for the application; `TimeProvider.System` is registered this way.
- **Transient:** a new instance per resolution; explain the term if asked, not as the lifetime of every service here.
- **Background worker:** creates scopes for work; does not hold one request's DbContext for its whole lifetime.
- **Testable time:** tests can supply controlled time; the presentation keeps the real clock.

DI wires dependencies. It does not replace permission checks or enforce module boundaries by itself.

## 7. Likely questions — short answers

**Why .NET?**

“I could already work effectively in it. ASP.NET Core, EF Core and built-in DI fit the domain rules and transaction requirements. Grilled demonstrates the Node integration boundary.”

**Why no microservices?**

“One engineer and one corridor did not justify distributed deployment and transactions. I would split a module for a proven independent scaling, release or ownership need.”

**Why two Flutter apps?**

“Passengers and drivers have different workflows and access. Separate apps keep driver features out of the passenger package; a small shared package provides common design pieces.”

**Why React for boards and Operations?**

“Both run in browsers. Their layouts differ: staff need forms and review; public boards need large, stable information.”

**How does a delay travel through the system?**

“Controller preview → permission/version checks → PostgreSQL change + audit + outbox transaction. Apps poll the new state. A worker sends the queued message to Grilled; it updates pickup timing and queues signed callbacks.”

**Is every screen updated instantly?**

“No. Normal refresh is 15 seconds on the web and 30 seconds on mobile. Users can refresh. Provider delivery is asynchronous.”

**Why polling? What is an ETag?**

“Polling is sufficient for this pilot's update rate and works across clients. An ETag identifies a response version; unchanged data can return 304. A successful poll does not make an old observation fresh.”

**What is the difference between schedule, observation, change and prediction?**

“Schedule: the plan. Observation: what was last seen. Change: an authorised staff decision. Prediction: an estimate calculated from those facts.”

**How is ETA calculated?**

“Scheduled time plus delay. Delay is the greatest of zero, reported delay, staff-confirmed delay and how overdue the next stop already is. Unknown evidence means no current estimate.”

**Service pattern versus journey versus observation?**

“Pattern is the repeating route and stopping plan. Journey is one dated run. Observation records that run's reported progress at a particular time.”

**Two controllers publish from the same version?**

“Only one conditional database version update succeeds. The other receives 409 and must preview the newer state. No silent overwrite.”

**Grilled accepted, but the response was lost?**

“The app retains one request key and payload. SmartRail stores one order link and reuses its ID with Grilled. Unique constraints and same-key reconciliation prevent a second order. Pending is not called success or failure before the result is known.”

**Does the outbox guarantee exactly-once delivery?**

“No. It retries delivery, so duplicates are possible. Stable event IDs and idempotent receivers prevent repeated business effects.”

**How are callbacks protected?**

“HMAC-SHA256 over timestamp plus exact body bytes, checked before parsing. There is a five-minute timestamp window, event-ID deduplication and status sequencing so late updates cannot move an order backwards. HMAC authenticates the message; HTTPS protects transport.”

**Who owns the food cutoff?**

“Grilled. SmartRail reads it; the provider rechecks it. Changing 30 to 45 minutes does not require a SmartRail app release.”

**Authentication versus authorization?**

“Keycloak proves who signed in. SmartRail decides what that account may do. It validates tokens, then checks local account status, permission and station or journey scope.”

**What are PKCE and JWT doing here?**

“PKCE protects the sign-in code exchange for public apps. The resulting access token is a signed JWT; the API checks issuer, audience, signature and expiry. A valid token alone does not grant railway permissions.”

**Why can't an administrator change a train?**

“Account administration and operational authority are different responsibilities. SysAdmin does not automatically get change-publication permission.”

**What prevents another passenger reading my order?**

“The server derives the account from the validated identity and checks order ownership. Changing an ID or hiding a button is not enough to bypass that.”

**What personal information goes to Grilled?**

“Order link, journey and stop references, timing and items. No passenger name, phone or email. Grilled generates the pickup code.”

**What happens offline?**

“Passenger: saved timetable, clearly aged, no live ETA or ordering. Driver: owner-bound saved briefing, read only; no offline server actions. Board: cached app and timetable, one offline banner. Operations: no offline writes.”

**How do you observe failures?**

“OpenTelemetry traces the API-to-provider calls. Aspire shows local logs, traces and health. Liveness means the process runs; readiness checks dependencies. A provider outage can make food degraded while train reads still work. Backlog count and oldest age expose delayed delivery.”

**How did you test?**

“Pure-rule tests for freshness and eligibility; PostgreSQL/API tests for transactions, permissions and races; Node tests and shared signed fixtures for the provider contract; Flutter/widget and browser checks for screens. Recorded device and browser evidence is separate from automated API evidence.”

Do not claim the entire five-flow acceptance pack passed: `_specs/pilot-app-acceptance.md` still has T2–T4 unchecked. Current CI builds/tests API and provider; it is not a complete browser/mobile pipeline.

## 8. AI, quality and ownership — 1 minute

Say only what matches your actual work:

> “I used AI for stakeholder simulation, requirement gaps, design exploration, small implementation tasks and review. I validated suggestions through business rules, tests and real screens. I remain responsible for the decisions.”

Concrete decision examples supported by the repository:

- Chose feature slices instead of copying the larger five-project module blueprint.
- Kept guest train browsing instead of a sign-in-first opening.
- Excluded AI-written operational notices: the pilot depends on structured, attributable facts.
- Kept server receipt separate from Control acknowledgement.

For an implementation defect, use the documented midnight report issue: yesterday's pending draft disappeared from the chooser. The fix retains its journey and request ID across dates; controlled-clock tests cover it. Full midnight device proof remains pending. **Do not call it an AI-caused bug without evidence of its origin.**

If asked for a rejected AI suggestion or diff, show an actual recorded example you personally reviewed. A prepared sentence is not evidence of your involvement.

Accessibility evidence includes generated token contrast checks, large-text/widget checks and recorded station browser scans. Audible TalkBack, native Sinhala/Tamil review and physical display-distance acceptance remain incomplete. Do not turn those into a full accessibility-certification claim.

## 9. Closing — 20 seconds

> “The main result is a connected information flow: a staff decision reaches the people affected, with a source and time. The pilot also makes uncertainty visible. My next priorities are finishing acceptance evidence, correcting local integration wiring and completing the remaining client slices before claiming production readiness.”

## 10. Rehearsal checklist — do before the evaluation

1. Start the local stack: `dotnet run --project services/apphost`. Use its actual API/provider addresses. Check database and provider readiness; plain API startup without a configured database is not proof the whole stack is ready.
2. Check real passenger, driver, controller, Auditor and kiosk sessions. Use separate browser profiles for staff roles, avoiding repeated sign-out on stage. Keep secrets off the projector.
3. Check today's seeded journeys, the driver's actual assignment, board station and the exact IDs used in the story. `--seed-demo --anchor` selects the Colombo **date** and prints a manifest; it does **not** shift all departures relative to the talk. SIM-3001's normal simulator window is **09:00–11:50 Colombo time**. Outside it, do not promise a live eligible food order.
4. If you need ready-made age examples, the existing Development command `dotnet run --project services/api -- --seed-operations-demo` creates SIM-OPS-FRESH, AGEING, UNKNOWN and OVERNIGHT examples. Use the configured database. These are **one-shot observations**, not continuously running simulator trains; the fresh row ages naturally. They use Fort → Ragama → Gampaha, not the food route or the driver's default assignment.
5. Rehearse one order all the way back to the passenger. The current AppHost sends Grilled to the API's HTTP endpoint; the recorded 19 October run encountered HTTPS-redirect/development-CA trust failure. That run used a temporary TLS-validation bypass that is **not** a completed wiring fix. If callbacks fail now, label the observed boundary and use recorded evidence; do not present a bypass as a security solution.
6. Check `/staff` login provisioning: AppHost does not currently configure `GRILLED_KIOSK_STAFF`. A page existing in source does not mean its collection-point password is configured.
7. Keep Operations on its registered login origin. Both Vite apps default to port 5173; use a separate board origin with matching API CORS, or the already rehearsed same-origin production proxy. Offline board reload requires the **production build**, not only Vite dev mode.
8. Prepare the unknown/offline example before the talk. Do not change the device clock against a real Keycloak session to force it.
9. Keep a short backup recording or dated screenshots of each successfully rehearsed step. Explain any API substitute for a missing screen.

### Current limits to state accurately

- Saved-route/follow/current-alert APIs exist; passenger saved-journey UI tasks remain incomplete. Do not demonstrate the decorative notification icon as a working alert inbox.
- Operations report, incident and audit screens exist. Demo-control APIs exist, but the Operations demo panel remains a placeholder.
- Food screens exist and have recorded Android evidence. The previous Ready/Collected device check used the kiosk **API**, not a fully verified kiosk-page-to-passenger journey.
- Midnight report recovery is fixed in code; its final device check remains open.
- Local, hosted CI, Android acceptance and Azure deployment are separate claims. This inspection does not establish an Azure deployment or production readiness.

## 11. Files to open for technical questions

| Question | File |
| --- | --- |
| Host and module wiring | [Program.cs](../services/api/Program.cs) |
| DI registrations | [OperationsSetup.cs](../services/api/Modules/Operations/OperationsSetup.cs), [AccessSetup.cs](../services/api/Modules/Access/AccessSetup.cs) |
| One context, module-owned mappings | [SmartRailDbContext.cs](../services/api/Infrastructure/SmartRailDbContext.cs) |
| Atomic change/version/audit/outbox | [ChangeConfirmer.cs](../services/api/Modules/Operations/Changes/ChangeConfirmer.cs) |
| Freshness and ETA | [RunningTypes.cs](../services/api/Modules/Rail/Running/RunningTypes.cs), [RunningCalculator.cs](../services/api/Modules/Rail/Running/RunningCalculator.cs) |
| Safe order retry | [OrderCreator.cs](../services/api/Modules/Catering/Orders/OrderCreator.cs) |
| Durable delivery and scope creation | [OutboxDispatcher.cs](../services/api/Modules/Catering/Outbox/OutboxDispatcher.cs), [OutboxWorker.cs](../services/api/Modules/Catering/Outbox/OutboxWorker.cs) |
| Callback authentication | [CallbackSignature.cs](../services/api/Modules/Catering/Callbacks/CallbackSignature.cs) |
| Architecture choice and trade-offs | [backend-decision.md](architecture/backend-decision.md) |
| Test/readiness evidence | [pilot acceptance](../_specs/pilot-app-acceptance.md), [driver](../_specs/driver-journeys.md), [food](../_specs/passenger-food.md), [station](../_specs/station-boards.md), [Operations](../_specs/operations-reports.md) |

Know four files well: **Program → OperationsSetup → ChangeConfirmer → OutboxDispatcher**. They explain wiring, DI, atomic business change and external delivery.
