# SmartRail: the 20-day scope decision

**Status:** Proposed scope freeze, made on 1 October 2026. The assessment deadline is Tuesday 20 October. This is the original 20-day plan; dates already passed need to be checked against actual progress.

**6 October change:** The passenger train-detail screen now includes a small route map. It replaces the separate stop-list display and uses the same simulated train updates. This is a deliberate scope change, not a claim of real GPS tracking.

**8 October change:** Kasun's screen feedback puts the route and next trains on the opening passenger screen. Anyone can check public train information without signing up or signing in. Seeded demo-account sign-in appears only when someone saves a commute, follows a train for personal alerts, or places a food order.

**8 October scope review:** The simulated stakeholder discussion highlighted three gaps. Add a small **“Train ends at [station]”** change; show three honest driver-report states; and prevent two controllers silently overwriting the same change. Treat claims that these were already built, tested or translated as unverified until there is real code, a demonstration and reader feedback. Keep the moving map marker and driver offline report queue as cuts if time runs short.

This decision came from two rounds of discussion with six AI reviewers, chaired by Claude. The [council record](ai-usage/2026-10-01-scoping-council.md) shows where they disagreed and where they were wrong. **You own the decision.** Change anything you disagree with and record why; that is useful evidence of how you used AI.

**Quick reading path:** read **Read this first**, **Exactly what we will build**, **What we deliberately leave out**, and **The original 20-day plan**. The other sections keep the detailed rules and evidence in one place for implementation and the viva.

## Read this first

| Question | Decision |
| --- | --- |
| **What problem are we solving?** | Staff confirm a delay, cancellation or platform change once. Passengers, drivers, station screens and the food provider all receive the same update. |
| **What makes it trustworthy?** | Every update shows its source and time. When train updates stop, the screen says **unknown**, never “on time.” |
| **What does a passenger see first?** | Gampaha → Colombo Fort, the next two or three trains, boarding-platform changes and the age of each update. This works without an account. |
| **What are we building?** | One .NET backend, two Flutter apps, two React web apps and a separate catering service: **six small deployables**. |
| **What are we designing only?** | The interactive station terminal, product AI and the wider three-month architecture. |
| **How do we prove it?** | Five automated journeys, centred on one service change reaching every relevant app and the caterer. |
| **When?** | The original plan aimed for submission on **20 October 2026**. Past dates now need a progress check. |

Food ordering proves that an external company can react to a railway change. It is not a second product. The existing smartrail-system-design-v2.html is the **three-month target design**; mark which parts were actually built in 20 days. That shows the assessors both the full system and an honest pilot.

**The one demo that must work:** a staff-confirmed change reaches the right people, and every screen handles old or missing data honestly.

**Terms used below:** an *end-to-end test* follows a whole user journey; an *API contract* describes exactly what two services send each other; an *outbox* is a database list of messages waiting for safe delivery; an *architecture decision record* (ADR) explains a technical choice. G0–G4 are project checkpoints, explained with the schedule.

## The eight-minute demo story

The people in the demo are Kasun (Gampaha–Fort commuter), Fathima (Tamil-first occasional traveller), Sunil (driver), Dilani (controller), and a RailBites kiosk worker.

The trains are fictional **SR-DEMO** journeys using real corridor stations. The seed-demo --anchor command schedules them relative to the presentation start; the app itself still uses the real clock. Fix the precise times at rehearsal, before the day-10 gate.

| Demo train | Its job in the story |
| --- | --- |
| A | Mid-journey; its next food collection station is about 50 minutes away. Used for an order, driver report and delay. |
| B | Its simulated position feed stopped five minutes before the talk. Starts as **unknown**, then recovers when the feed resumes. |
| C | Leaves Fort about 15 minutes into the talk. Used to show a platform change and cancellation; an early-ending scenario can be shown separately. |
| D | Reaches a food collection station during the talk. Its order was placed earlier, so staff can show collection. |

1. Kasun opens Gampaha → Colombo Fort without signing in and sees the next trains, boarding platform and update times. Fathima finds A, signs in only when she orders lunch for collection at Peradeniya. RailBites confirms the order and gives a pickup code.
2. Sunil reports a signal failure from A's driver app using a short reason form.
3. Dilani opens an incident, enters a 25-minute delay, sees which stops, screens, saved journeys and food orders will be affected, then publishes it.
4. The passenger app shows the delay and who confirmed it. Peradeniya's entrance board shows it in Sinhala, Tamil and English. The driver briefing updates. RailBites and Fathima see the new arrival and collection time.
5. B shows **“Arrival unknown — last seen at Veyangoda at 10:02.”** Its route map shows the stations but no current train marker. Food ordering for B is blocked. Resume its feed to show the marker and arrival estimate return. The simulation console can pause it again if the panel asks.
6. D arrives; kiosk staff enter the pickup code; Fathima sees **Collected**. Dilani resolves the incident, the auditor sees the history, and the system administrator gets a **403** (permission denied) when trying to change a train's status. That denied attempt is also audited.

**Optional failure demo:** Make RailBites return 503, then cancel C. Train information still works. The health panel shows the pending catering message. Restore RailBites; the message is delivered and the order is cancelled.

**Early-ending drill:** End a train at Polgahawela after a passenger has ordered food for Peradeniya. The passenger app, station board and driver briefing say where the train now ends. Earlier valid stops stay visible; Peradeniya loses its arrival estimate and new food-order option. The existing Peradeniya order is cancelled with a reason. The pilot has no refund step because payment happens at collection.

## Exactly what we will build

### Passenger app — Flutter

**Build:** a public first screen starting at **Gampaha → Colombo Fort**, with an easy route change and the next two or three trains. Each row shows scheduled and estimated arrival at Gampaha, delay, boarding platform and last-update time. Put cancellations, early endings and boarding-platform changes above the list. Highlight the earliest expected arrival at Colombo Fort only when its estimate is current, it stops at both stations and it is not cancelled or ending early before Fort. Train detail shows the last known station, stops and a small route diagram with a readable stop list. Food has its own tab with menu, order, pickup code and status. Keep seeded demo-account sign-in for saving a commute, following a train for personal alerts and ordering food.

**Keep simple:** open straight to the guest train list. Any sign-in screen reached from a personal action offers **Continue without an account** back to that list. Show public route notices to everyone and personal in-app alerts to signed-in followers only while the app is open. Cache the public timetable for guests and personal saved information for signed-in users, each with age labels. Translate status words on home and train detail. Label current information **Current simulated updates** and old information **Saved timetable / offline**. A confirmed platform change keeps its own confirmation time even if the train position becomes unknown.

**Do not build:** a street-map or GPS tracking service, background push notifications, account registration, connection planner, a separate low-data setting, crowding, tickets or seats. The route map is a light graphic with no map tiles or photos.

### Driver app — Flutter, separate from the passenger app

**Build:** an assigned-journey briefing with stops, times, confirmed changes and notices, including issuer and time. Cache it when the driver signs on and show when it was downloaded.

**Keep simple:** a reason-based field report in no more than three taps. Show **Not sent** before delivery, **Sent — awaiting Control acknowledgement** after the server receives it, and **Acknowledged by [name] at [time]** only after a controller takes that action. An offline queue for non-urgent reports is optional; if cut, offline reports stay **Not sent**.

**Design only:** a way to prevent input while the train is moving.

**Do not build:** engine-condition records, defect photos or chat. The app **never** gives movement authority or speed orders.

### Operations web app — React

**Build:** a corridor table with train, last station, age of update, delay and status; show **unknown** when stale. Staff can compose a delay, cancellation, platform change or **“Train ends at [station]”**, see its impact, publish it and leave an audit trail. A second edit based on an old version must not silently replace a newer change; return **409** and ask the editor to reload.

**Keep simple:** incident states Open → Resolved, a field-report inbox with a separate Control acknowledgement action, an auditor view, system health, and a simulation console.

**Do not build:** paper-versus-app conflict handling, a screen for temporary role grants, maintenance routing, reports, or timetable editing. The timetable comes from a curated seed file.

### Station displays — React in kiosk mode

**Build:** a platform view and an entrance overview, with a short delay reason on affected rows. Show Sinhala, Tamil and English together, a clock, a simulation footer, and an offline state that says **“Live updates unavailable”** while showing the timetable.

**Design only:** five wireframes and an interaction spec for the interactive terminal, including language choice and offline behaviour. Describe the waiting-area board as the entrance view plus an explanation.

**Do not build:** free-text station notices or staff corrections to the board.

### RailBites — a separate Node/TypeScript service with SQLite

**Build:** provider-owned collection points and cutoff policy, an orders API that is safe to retry, signed order-status webhooks, and a receiver for train-change events.

**Keep simple:** a kiosk staff page grouped by station, where orders move through Ready, Collected and No-show; a switch that makes the service return 503 for the failure demo.

**Do not build:** online payment (pay at the kiosk), pushed stock changes, order editing or ratings.

### Backend and notifications

The **.NET 10 backend** has Network, Live, Changes, Catering and Identity/Audit modules, plus the simulator. Do not add Redis, Service Bus, separate workers or an onboard edge service.

Notifications are an **in-app passenger feed**, a catering webhook, a change list on the driver briefing, and updated station boards. No Firebase push, SMS, email or PA integration.

## How the system will work

These are the technical decisions behind the scope. The D-numbers let us refer back to them in the presentation.

**D1 — One backend.** Use a .NET 10 **modular monolith**: one deployed backend divided into clear modules, with PostgreSQL. Each module owns a database schema. An architecture test prevents one module reaching into another's internals. Keep API endpoints as small, complete features using EF Core's DbContext directly for database access. This reuses .NET skills from the library assessment and avoids a microservice deployment, Redis and Service Bus.

**D2 — Separate apps.** Make two Flutter apps with separate app IDs, sharing a small smartrail_core package. Make the operations and station apps in React. Driver-only features should not ship inside the passenger app.

**D3 — Four kinds of train information.** Keep **schedule** (published plan), **observation** (last report), **confirmed change** (staff decision), and **prediction** (calculated estimate) distinct. Calculate predictions when requested; do not store them as permanent facts. A confirmed change takes priority, then a valid prediction, then the schedule. An early-ending change names the new final station: earlier stops remain valid, while later stops have no arrival estimate or food collection eligibility.

**D4 — Simulator, ETA and map.** The simulator sends a position every 30 seconds through the same input route a future real feed would use. Each position gives the section between two stations and approximate progress along it, not GPS coordinates. The passenger route map draws this progress from the train-detail response. ETA is scheduled arrival plus the current delay. Current delay is the greatest of observed delay, staff-confirmed delay and time already overdue at the next stop. A simulated train never leaves early. Station-only reports would be too far apart for the four-minute freshness rule, so the simulator sends positions between stations. The map says **SmartRail simulation** and shows when the position was last updated.

**D5 — Freshness.** At three minutes since the underlying observation, show that information is ageing. At four minutes, show the observation-dependent position and ETA as **unknown**; hide the moving marker. A separately confirmed platform change stays visible with its own confirmation time. SmartRail blocks new food orders when train status is unknown; it does not send a new “unknown” event to RailBites. The client calculates age from the observation timestamp, correcting for the difference between server and device clocks. The server supplies the thresholds. The same examples are tested in C#, Dart and TypeScript. Older two/ten-minute and three-minute rules in the design visualiser are retired.

**D6 — Updates to screens.** Polling is the dependable path: station and operations screens every 15 seconds; mobile every 30 seconds, plus refresh on reopening or pull-to-refresh. Use ETags (version labels) so unchanged answers are cheap. A one-way server hint saying only “something changed” is optional. No SignalR; it would add a Flutter dependency and more server setup.

**D7 — Real external boundary.** RailBites runs separately in Node/TypeScript and owns its SQLite database. Write its OpenAPI contract (the agreed API shape) first. Do not share code classes or data objects across the boundary. The passenger app calls only SmartRail; SmartRail calls RailBites.

**D8 — Safe retries and webhooks.** Every order request has an idempotency key, so retries do not make two orders. A PostgreSQL **outbox** stores messages until RailBites receives them, retrying with increasing delays. Status callbacks (**webhooks**) have an HMAC-SHA256 signature, a secret-key check over the timestamp and exact raw message. Reject timestamps outside five minutes, repeated event IDs and old status sequence numbers. Test signed example messages in both services. A separate Pact setup is unnecessary when one person owns both sides.

**D8a — Staff edit conflict.** A service-change draft carries a version. If another controller has already saved a newer version, the old draft returns **409 Conflict**. Show the user the latest version and require a fresh review before saving. This applies to the change itself; the existing order-cutoff 409 is a different rule. Demonstrate a two-controller conflict before claiming this protection in the viva.

**D9 — Login and permissions (revised 14 October).** Public station, timetable, train-status and disruption reads explicitly allow guests; rate-limit these reads. Saved commutes, following for personal alerts, food orders and all staff actions require a signed-in account with the right permission. **Keycloak is the identity provider** for passenger and staff sign-in. Browser and Flutter clients use OpenID Connect Authorization Code with PKCE; the API validates Keycloak's issuer, signing keys, audience and token lifetime. Keycloak owns credentials and refresh-token lifecycle; SmartRail does not issue tokens or store password hashes or refresh tokens. SmartRail keeps a local account linked by issuer and subject and owns its role-to-permission map, station scopes and driver assignments. Deny by default; check local account status and scope on authenticated requests. Seed demo users and roles reproducibly. Use separate service authentication for RailBites callbacks as described in D8. See [identity and local development](architecture/identity-and-local-development.md).

**D10 — Offline behaviour differs by app.** Passenger: cached public timetable works for guests; signed-in saved information stays private. Both show age labels. The route map keeps its stations but hides the train marker when the position becomes unknown; no food order while offline. Driver: cached briefing and, if time allows, an outbox for non-urgent reports. Station: a service worker keeps the last board and falls back to timetable-only display. Operations: no offline writes. Old data never appears current. No mobile database or train-side edge computer.

**D11 — One Azure environment (revised 14 October).** Run the API, RailBites and Keycloak as separate Azure Container Apps for the pilot; Keycloak needs a stable public HTTPS issuer and a persistent database. Keep one API replica, one RailBites replica and at least one Keycloak replica during the live demo. Use separate SmartRail and Keycloak databases on PostgreSQL Flexible Server; never put Keycloak tables in SmartRail's schemas. RailBites' SQLite file also needs a tested persistent storage plan; a Container App's writable filesystem disappears on restart. Keep the free Static Web Apps tier. **.NET Aspire AppHost is the local development and local demo stack** for the API, RailBites, PostgreSQL and Keycloak, with its dashboard for local telemetry; it is not the Azure production orchestrator. Cap Application Insights data and set budget alerts. Re-estimate Container Apps memory and database cost with Keycloak before relying on the USD 200 credit; the earlier B1ms assumption is unverified for this larger stack. The original plan says to delete Azure resources after 20 October. Three environments, Bicep and Key Vault are deferred.

**D12 — Know when it breaks.** Instrument the API and RailBites with OpenTelemetry. Send local telemetry to the Aspire dashboard and Azure telemetry to Application Insights, including a trace across the catering boundary. The dashboard is a local viewer, not durable production storage. Return a correlation ID (a request tracking number) with API errors. Add /health/live and /health/ready; if RailBites is down, report the API as **Degraded**, not Down. Watch feed staleness, rejected webhooks, outbox backlog and when each display was last seen. Check Keycloak availability separately; existing JWTs can still be validated while login is unavailable. Have one alert, a runbook and three measurable reliability targets. No Prometheus/Grafana stack.

**D13 — Languages.** Put translation plumbing in every app. Translate about 40 fixed status and reason phrases into Sinhala, Tamil and English, and have native readers check them. Put all three together on public displays; use them on passenger home and detail. Staff apps stay in English for the pilot, explicitly as an assumption. Do not machine-translate free text. Bundle Noto fonts.

**D14 — No AI inside the shipped product.** The earlier AI notice writer stays in the target design only. Record it in the threat model as considered and rejected for this pilot. The current product promises statements with a source and time; an LLM could invent an operational fact or be manipulated by text it reads. No OpenRouter chatbot or notice writer in this build.

**D15 — Small shared design system.** Keep colours, type and spacing in tokens.json, and status meaning in status.json. A script of about 40 lines generates CSS variables and Dart values. Build two reusable pieces: StatusBadge and FreshnessLine. Use colour mainly for exceptions. Target WCAG 2.2 AA, with 7:1 text contrast on displays and the driver app. The same status language appears everywhere, while each screen uses a layout suited to its job. No Style Dictionary or automatic Figma sync.

## Food ordering rules

RailBites owns the **menu, collection points, order, stock, cutoff and payment**. In the demo it offers Polgahawela Junction and Peradeniya Junction. SmartRail offers a collection point only if it is still ahead of the train.

- A passenger may order for a journey on the same day until **estimated arrival minus the provider's cutoff**. The default cutoff is 30 minutes; RailBites owns the setting. Do not take orders when train status is unknown. The live-change drill changes the cutoff to 45 minutes in RailBites only.
- Orders cannot be edited. Before the cutoff, a passenger can cancel freely and place a new order. After it, cancellation is unavailable.
- If the train is delayed, SmartRail sends the confirmed new ETA from its outbox. The order stays with that train and the ordering window extends. If the train is cancelled or stops before the collection station, the provider cancels the order and the passenger sees why.
- For an early ending, keep valid stops before the new final station. Do not offer collection beyond it. Send the confirmed change to RailBites so it cancels **existing** orders beyond it and tells the passenger why. The provider does not need an event when a position simply becomes stale.
- If the passenger does not collect it, the provider marks it **No-show** after the train leaves. Show that policy before the order is confirmed.
- Passengers pay **at the kiosk**. SmartRail handles no card details, and this pilot has no automatic refund. The provider supplies the menu; SmartRail may cache it for 60 seconds, but the provider checks stock again when the order is submitted. If an item is gone or the provider rejects the order, show its reason. Stock changes are fetched when browsing and checked at submit; pushing them is deferred.
- SmartRail stores the link between the passenger and the provider's order reference. RailBites receives the dated journey ID, train number, collection station, scheduled and estimated arrival, items and pickup code. It receives **no passenger name, phone number or email**.
- RailBites gives the pickup code. Kiosk staff enter it to mark collection, and a signed status webhook changes the passenger's card to **Collected**.
- SmartRail never caches the provider's cutoff. Both sides use the same ETA, kept in sync by the outbox. If SmartRail thought an order was allowed but RailBites rejects it, show the provider's reason.

## Who can do what

These are **pilot roles**, not a claim about Sri Lanka Railways' official OMS role codes.

| Role | Allowed work | Limit |
| --- | --- | --- |
| Guest | Read public stations, timetables, train status and disruptions | Cannot see personal data, save a commute, follow for personal alerts or order food |
| Passenger | Read public information; save and follow trains; receive personal in-app alerts; place, view and cancel food orders | Their own saved data and orders only |
| Driver | Read a briefing and file a field report | Assigned journeys only |
| Controller | Publish or withdraw service changes; manage incidents | Pilot uses network-wide access |
| StationStaff | Publish a platform change through the same audited process | Their station only |
| Auditor | Read incidents and the audit history | No operational edits |
| SysAdmin | Manage SmartRail account status and role grants; Keycloak identity provisioning is separate | Cannot change train status |
| SimOperator | Pause, resume and inject simulated train events | Console exists only when Simulation:Enabled=true |
| RailBites service | Send order-status webhooks and check eligibility | Its own orders |

The demonstration shows a controller publishing (E2E-1), a driver reporting (E2E-5), an auditor reading the trail, and a system administrator being denied with **403**. Security tests also try a station user at another station and a passenger trying to read someone else's order.

Customer Information Officer, Timetable Curator, Maintenance and Manager/Reporting roles are **documented only**. Adding one later should be a row in the role-permission table and its test matrix.

An **incident** is an internal operational cause. A **service change** is the public message that passengers see. One incident can lead to several service changes.

## How we will prove it works

The biggest risks are shared rules: when train data becomes old, what a confirmed change means, who may publish it, and what a food order does when the train changes. Test these rules in the .NET backend with a controllable clock and real PostgreSQL. Test freshness with shared examples in C#, Dart and TypeScript. Check the catering contract from both services.

There are **five automated end-to-end journeys**. Playwright covers the connected system, calling the same API used by Flutter when a step belongs to a mobile app. Widget tests cover the mobile screens. One disruption flow also runs as a Flutter integration test against the local Aspire stack, recorded before G4, so the evidence includes an actual mobile app. Mobile E2E does not run in CI.

1. **Delay reaches everyone:** a guest opens the next-trains view without a token; a controller publishes a delay; the passenger API, driver briefing and station board show the same confirmed-by and confirmed-at details, plus the simulation label; the audit record exists.
2. **Old data is called old:** stop a train's feed; show ageing at three minutes and unknown at four. The passenger map must hide its train marker at four minutes and restore it only after a new position arrives. Then disconnect the display, show **Live updates unavailable**, reconnect and recover. Use test clocks rather than waiting four real minutes.
3. **Food follows the train:** order food, publish a delay, verify RailBites gets the new ETA, collect with a pickup code and verify the signed webhook updates the passenger card.
4. **Cancellation and early ending reach orders:** cancel a train, then end a different train early. RailBites cancels existing orders at stations the affected train will not reach; public screens name the change and keep valid earlier stops.
5. **Driver report becomes an incident:** driver files a report; delivery first shows **Sent — awaiting Control acknowledgement**. A controller actively acknowledges it, so the driver sees a name and time. The controller opens an incident, publishes a change and resolves it; the auditor sees the history.

**Tests below that level:** xUnit checks ETA, precedence, freshness, early-ending stop eligibility, order cutoff and incident states. API integration tests use WebApplicationFactory and Testcontainers PostgreSQL. They cover public reads without a token, denial of guest access to saved commutes and orders, an audit row with each change, outbox delivery, RailBites recovery, cancellation of existing orders beyond an early-ending station, an order-cutoff 409, and a separate two-controller edit-conflict 409. A report test distinguishes **Not sent**, server receipt and a real controller acknowledgement with name and time. A security test lists every endpoint and fails if its permissions are missing. It also checks cross-station access, someone else's order, expired temporary access, bad HMACs, replays and duplicate messages. The committed catering OpenAPI file, webhook schemas and signed sample messages are checked by .NET and Node. Flutter widget tests cover every home-screen state and built-in accessibility checks; Vitest covers display freshness and offline components. Change the stale threshold from four to five minutes once as a spot-check and record which tests fail.

**QA evidence:** keep a defect log with the source of each defect (AI code, your code, or a missed requirement). Upload Playwright reports and traces from CI. Save evidence on days 10, 16 and 19. Put requirement IDs in test names so a script can build a traceability matrix. On day 17, write a two-page quality review with an ISO 25010 scorecard, open defects and an honest verdict: **fit for a pilot demo, not production, because…** Load, chaos and device testing are described rather than run; pilot-scale results would not change the build decision.

### Security and privacy

Draw one data-flow diagram with four trust boundaries: public clients, staff devices, Azure and RailBites. Use STRIDE to list threats, and link each threat to a control and test. Make version 1 on day 3 and version 2 on day 16.

**Build these controls:** Keycloak login; issuer/audience/signature/expiry validation; deny-by-default permissions with station/journey scope; an authorization test for every endpoint; input validation and plain API error responses without stack traces outside Development; rate limits for public reads and Keycloak brute-force protection for login; signed catering webhooks; order idempotency; audit rows committed with the change; HTTPS, CORS allowlist and a Content Security Policy. Keep local secrets in Aspire parameters or user-secrets, CI secrets in GitHub, Azure secrets in Container Apps, and mobile tokens in flutter_secure_storage. Run gitleaks, Dependabot, .NET vulnerable-package checks and npm audit.

Run ZAP API and baseline scans on days 10 and 16, record how each finding was handled, and ask AI to review authentication and webhook code. Log which AI findings you accepted or rejected. MFA/SSO, Key Vault, WAF, mutual TLS, device management, certificate pinning, a penetration test and a restore drill remain **documented future work**.

| Data | Examples | Pilot retention | Who sees it |
| --- | --- | --- | --- |
| Public | Stations, timetable, disruptions, simulated positions | Indefinite | Everyone |
| Passenger personal | Email and credential data in Keycloak; local account reference, saved journeys and order references in SmartRail | Until deleted; orders 90 days | The passenger; staff see counts only |
| Operational | Incidents, driver reports, audit rows, staff IDs | Audit one year; positions 30 days | By role; auditor can read audit |
| Sent to RailBites | Pickup code, journey, station, arrival times, items | RailBites' policy | RailBites |
| Secrets | Keycloak admin and database credentials, provider HMAC secret | As required | Deployment pipeline |

Never log request bodies, authentication headers, email or phone. Run an order flow into a test log sink and fail if email or phone patterns appear. Cite Sri Lanka's Personal Data Protection Act No. 9 of 2022 in the privacy note, and **check which provisions are in force** rather than assuming.

### Accessibility and user testing

Run axe in the web end-to-end tests and fail CI for serious or critical findings. Test design-token contrast and use Flutter's built-in checks for tap targets and contrast. Manually walk guest passenger home → train detail → unknown with TalkBack; operate the service-change form by keyboard with NVDA; inspect 200% text; check displays with colour-vision emulation. In a three-second glance check, ask passengers to find the next train, its boarding platform and the age of its update; record misses and revise the screen. Ask Sinhala and Tamil readers to check the actual phone and station screens, including text that might be clipped. Record their feedback before claiming those languages are ready. Test the built app in a hallway on day 17 and check whether the display can be read from four metres.

## Design work and screen rules

Timebox design to about **1.75 days**. G0 is a time limit, not permission to delay implementation: if the polished screens are not done by Sunday 4 October, build from the wireframes. Make basic wireframes for every surface by day 3, including the terminal and RailBites; draw flows in Mermaid. Polish only four screens: passenger home, train detail in three states, platform board and entrance board. Build operations web with the shared tokens and ordinary controls.

At G0, have tokens.json and status.json for all eight states and their icons, a passing contrast script, a status sheet using long Tamil placeholder text, and the four polished screens. Get native-reader feedback on translations by day 9 and freeze strings on day 12. After G0, **the code is the source of truth**; do not try to keep Figma and code automatically synced.

**Passenger first-screen rules:** open directly on the route and trains, with no sign-in wall or scenic image. Keep route change at the top, scheduled and estimated times separate, update age visible on every train row, and boarding-platform changes prominent for the chosen origin. Ask for the seeded demo account only when a personal action needs it; that sign-in screen has a clear guest return. Keep Food in its own tab. When offline, label saved information and remove unreliable ETAs. These rules supersede the earlier sign-in-first image concept.

**Busy-station rules:** time, platform and icon carry the first meaning; destination and status use words. Each row reads **Scheduled · Destination · Platform · Status**, with expected time shown separately. Never erase the scheduled time. No carousels, tickers, flashing or other movement. Every display has a clock and one fixed simulation footer. If one train is stale, mark that row. If the whole feed is lost, show one banner and make every row timetable-only; do not mix the two warnings.

**Driver screen rules:** default to a light theme for sunlight, replacing the dark theme in v2. Use 20sp text, 28sp times and 56dp touch targets. Put the radio instruction on the report and offline screens, not everywhere. Use three distinct report states: **Not sent — Control has not been told**, **Sent — awaiting Control acknowledgement**, and **Acknowledged by [name] at [time]** after a controller action. If the optional offline queue is cut, keep the first state and urgent-use-radio wording. Urgent reports such as obstruction, injury or inability to continue can **never** sit in an offline queue.

## What we deliberately leave out

The presentation should give an evidence- or risk-based reason for each cut, say what works instead and say when the idea deserves another look. Avoid the bare phrase “out of scope.”

The five most important exclusions to explain are:

| Not built | Why, and what we do instead | Revisit when |
| --- | --- | --- |
| Real SLR train positions | All moving trains are simulated and labelled. Only published schedules come from real sources. | SLR offers an authorised feed |
| Safety-critical driver instructions | Speed orders and movement authority require official rules, reliable data and safety approval. The app offers an informational briefing only. | Railway approval exists |
| Online payment | RailBites owns the order and takes payment at the kiosk; SmartRail handles no card data. | SmartRail becomes the seller |
| Interactive terminal | The assessment accepts station displays. Terminal wireframes and offline interaction rules still exist; a future terminal can reuse the station-board API. | Displays prove useful |
| Background push alerts | The decision about who should be notified is built. In-app delivery avoids device tokens and extra operations. | Background alerts are shown to be needed |

Other deliberate cuts:

- **Product AI:** no chatbot or notice writer in the build (D14).
- **Street map and GPS tracking:** the passenger app builds a simple route map from stations and simulated progress. It does not imply an exact real-world train location or need a map provider.
- **Moving route marker if time runs short:** keep the station diagram and last-seen text. Hide the marker when the observation becomes old; remove marker animation before cutting freshness or early-ending behaviour.
- **Engine condition and defect photos:** no maintenance records to trust; photos need storage and malware checks.
- **Driver–control chat:** a short report and acknowledgement are enough for the pilot; urgent matters use radio.
- **Paper/app conflict workflow:** we have not verified the real paper procedures.
- **Role-grant screen:** SmartRail retains application-specific station and journey grants; `validUntil` is checked and tested if time permits. Keycloak handles sign-in, not these operational scopes.
- **Passenger registration:** use demo users seeded in Keycloak; self-registration remains cut.
- **Separate waiting-area display:** the entrance board gains a delay explanation.
- **Separate low-data mode:** the route map uses a small built-in graphic with no downloaded tiles or photos. ETag/304 polling also reduces data use.
- **Redis, Service Bus, three environments, Bicep and Key Vault:** one replica and corridor-scale load can use a PostgreSQL outbox. ADR-10 records when that should change.
- **iOS build:** demonstrate Android; Flutter leaves iOS possible later.
- **Mobile E2E in CI, Pact, k6 and chaos tools:** focus on API rules, client widgets and the five cross-app flows; record simple capacity arithmetic.
- **Automatic PDF timetable import:** use a hand-checked seed file with its source.
- **Onboard edge service:** offline support belongs to the passenger and driver devices, not train hardware.

## The original 20-day plan

This schedule was written on **1 October**, before work began. It assumes work on Saturday 3, Sunday 4, Saturday 10 and Saturday 17 October. Sundays 11 and 18 are buffers. Without those working weekends there are only 14 days, so the scope would need further cuts. **Because today is later than 1 October, compare every past-day target with actual progress before treating these dates as a current commitment.**

G0–G4 are checkpoints: G0 design timebox, G1 first Azure deployment, G2 the display and passenger loop working on Azure, G3 feature freeze, and G4 final evidence check.

| Day | Date | Finish that day | Proof or checkpoint |
| --- | --- | --- | --- |
| 1 | Thu 1 Oct | Freeze scope; short PRD; acceptance criteria for five journeys | This decision, council record, AI log with an “overrode” column |
| 2 | Fri 2 | Draw journey, freshness, order and incident states; agree roles and data classes; start design tokens, repository, local stack and CI | ADR-01, test strategy; CI green |
| 3 | Sat 3 | Draft both OpenAPI contracts; system and Azure diagrams; cost plan; basic wireframes; recruit testers and translators | Threat model v1, ADR-02, cost sheet |
| 4 | Sun 4 | Timebox four polished screens; test contrast; send wording to reviewers | Figma and status sheet; **G0** |
| 5 | Mon 5 | EF Core model, migrations, sourced seed data, demo seeder, read APIs and authenticated permissions | Domain and auth tests |
| 6 | Tue 6 | Deploy API, PostgreSQL and a Static Web Apps shell to Azure; add tracing, caps and budget alerts | Public health URL, ADR-03; **G1** |
| 7 | Wed 7 | Deterministic simulator, clock injection, status and ETA rules, 3/4-minute rule, ETag endpoints | ADR-04 and freshness tests |
| 8 | Thu 8 | Operations corridor view and change composer; define early-ending and controller-edit conflict behaviour; station board polls | **E2E-1** |
| 9 | Fri 9 | Shared Flutter core; passenger next trains, status and detail with the simple route map; translated strings from the start | Widget tests, including map freshness |
| 10 | Sat 10 | Platform and entrance boards, three-language status, offline banner; passenger cache and in-app alerts | **E2E-2**, first ZAP scan; **G2 on Azure** |
| 11 | Sun 11 | Rest if G2 passes; otherwise apply the agreed cuts | Record cuts |
| 12 | Mon 12 | RailBites API, policy, SQLite, signed webhook, staff page; SmartRail catering module; order screens | **E2E-3**, ADR-05, signed samples |
| 13 | Tue 13 | Outbox, retry, 503 mode, idempotency, cutoff, No-show and cancellation | **E2E-4**, failure and cutoff tests, data-sharing note |
| 14 | Wed 14 | Driver cached briefing, report reasons, distinct sent and Control-acknowledged states; optional offline queue | Widget tests |
| 15 | Thu 15 | Simple incident workflow, report inbox, station permissions, optional validUntil | **E2E-5**, security suite, first viva drill; **G3 freeze** |
| 16 | Fri 16 | All five E2E journeys green in CI from a fresh seed; Flutter integration recording; security and accessibility passes | Second ZAP scan, threat model v2, defect log |
| 17 | Sat 17 | Hallway tests and wording fixes; architecture, ADR, privacy, runbook, SLO, QA and AI-use documents | Test notes and quality review |
| 18 | Sun 18 | Buffer for high-priority defects; check all evidence links | **G4** |
| 19 | Mon 19 | Final deploy, demo reset, backup video and two rehearsals | Second viva drill; **code freeze at noon** |
| 20 | Tue 20 | Present and submit | Submission receipt |

### Where the 20 days go

Each build estimate includes its unit tests. These are **AI estimates**, not measured work.

| Work | Days | Work | Days |
| --- | ---: | --- | ---: |
| Backend core | 2.25 | Auth | 0.5 |
| Realtime updates | 0.5 | Simulator | 1.0 |
| Operations web | 1.75 | Passenger app | 2.25 |
| Driver app | 1.0 | Station display | 0.75 |
| Interactive terminal | 0 | Catering | 1.5 |
| End-to-end tests | 1.25 | Requirements | 1.25 |
| Docs, diagrams and decisions | 1.25 | Figma | 0.75 |
| Deployment | 0.75 | Security testing | 0.25 |
| Presentation | 1.0 | Buffer | 2.0 |
| **Total** | **20** | | |

### Cuts already agreed if a checkpoint fails

| When | If this happens | Do this |
| --- | --- | --- |
| Day 2 | CI is not green | Use plain folders and one CI workflow; drop extra monorepo tooling |
| G1, day 6 | Azure is not running | Continue in Aspire; retry once on day 16; prepare a local demo and backup video |
| Day 7 | Simulator is not repeatable | Replace it with an **Advance train** button in operations |
| G2, day 10 | Flutter cannot use the API | Make driver a role-gated React route, which the brief allows as an equivalent; put Sinhala/Tamil on displays only |
| Day 10 | Passenger offline cache is unfinished | Keep age labels and unknown states; drop downloaded timetable |
| Day 13 | Catering round trip fails | Drop RailBites staff page first; keep outbox, new ETA delivery and one passenger order screen |
| G3, day 15 | Work is slipping | Drop validUntil, driver offline queue, unfinished moving-map marker and SSE hint before core freshness, early ending, report acknowledgement or reviewed language. Add nothing new after G3 |

## Keep AI-written code understandable

1. Give an AI agent one small change at a time, about **300 reviewable lines maximum**.
2. Write every commit message yourself: what changed, why, and which suggestion you rejected.
3. Personally understand and check status/ETA/freshness, Keycloak token validation versus SmartRail permissions, HMAC checks, and food eligibility/cutoff. These are likely viva questions. Keep HMAC checking and the outbox small enough to explain line by line.
4. Put conventions in CLAUDE.md or AGENTS.md: one endpoint pattern (endpoint, validator, handler, direct DbContext); Riverpod in Flutter; TanStack Query in React. Do not add MediatR, generic repositories, CQRS layers or a new dependency without recording a decision.
5. Keep an AI log with date, suggestion, your choice, reason and commit. Aim for at least 12 entries, including three rejected code suggestions linked to diffs and one AI-written bug caught by a test.
6. Spend 15 minutes daily updating the tracker, saving two screenshots, logging AI decisions, recording test count and explaining one random file aloud.
7. Rehearse without AI on days 15 and 19: change RailBites' cutoff from 30 to 45 minutes; add a Customer Information Officer permission; change staleness from four to five minutes and update the now-failing tests; add a reason code in all three languages.

## Questions to be ready for in the presentation

1. A controller makes a train 20 minutes late. Trace its path through every store and app.
2. Make the catering cutoff 45 minutes. Which system owns that rule?
3. Add a role that can publish notices but cannot change train status.
4. The simulator stops. What does each app show after three, four and ten minutes? Which clock decides? Show a test.
5. RailBites accepts an order but its response times out. Why does a retry not create a second order?
6. A 40-minute delay happens after ordering. Who decides what happens to the food?
7. A passenger changes the order ID in a request. Why can they not see another person's order?
8. Why .NET after training with Node? What would justify splitting the monolith?
9. Explain the difference between a repeated service pattern, a dated journey and a train observation.
10. Show an AI suggestion rejected in code, with the diff.
11. Exactly what passenger/order fields go to RailBites, and what can appear in logs?
12. Two controllers edit the same change. Show the second save returning **409** and requiring review of the newer version. If this is not implemented and demonstrated, say so plainly rather than claiming the protection exists.

## Document updates still needed

- Mark each item in feature docs 01–05 as **build, build thin, design only or cut**, following this scope. The terminal in doc 04 is design only; the waiting-area board in doc 05 folds into the entrance board.
- Relabel smartrail-system-design-v2.html as the **target architecture**. Update .NET 8 to .NET 10, SignalR to polling with an optional SSE hint, and freshness to three/four minutes. Show the driver app with a light default theme. Mark onboard edge and the AI notice writer **target only, not built**.
- Update the tracker gates: G1 6 October, G2 10 October, G3 15 October, G4 18 October. Delete S11-terminal, S16-low-data and S17-AI. Change S14 to **report and acknowledgement**. Add an “overrode” column to the AI log.
- Add contracts/, docs/qa/, docs/design-system.md and the architecture decision records.

## Decisions only you can confirm

- Can you work on **3, 4, 10 and 17 October**? If not, cut more.
- Can Sinhala and Tamil readers review the wording by day 9? If not, show only reviewed strings and explain that limit.
- Does the assessor expect Node for the backend? The council chose .NET because you have already used it; the brief allows another stack unless extra constraints exist.
- Is the **USD 200 Azure credit** active now?
- Is **Android only** acceptable for the demo? Flutter keeps iOS possible, but no iOS build is planned.
- Check the AI estimates for work and Azure cost, including the proposed **USD 40–60 per month**, against actual prices and usage.
