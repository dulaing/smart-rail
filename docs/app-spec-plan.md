# SmartRail app specs: plan and agent handoffs

Prepared 18 October 2026. These are **recommended spec briefs**, not approved implementation plans or completed features. Use them to start a `$spec` conversation in this repository. The deadline is still 20 October; confirm any change rather than relying on the old day-by-day schedule.

## How to use this

Start another agent in `C:\Projects\260929-SmartRail`. Copy the prompt for the next feature below. Every prompt tells the agent to read the common context in this file, so you do not have to explain the project again.

Generate and discuss one spec at a time. Read its decisions before implementing it. Finish a visible slice before generating more detail for later slices. The prompts request planning only; implementation follows through `$task`.

Do not create one giant spec per app. Passenger browsing, personal alerts and food have different rules and dependencies; Operations also needs more than one feature spec.

## Recommended order

| Order | Proposed spec in `_specs/` | Visible result | Depends on |
| --- | --- | --- | --- |
| 1 | [app-foundation.md](../_specs/app-foundation.md) | Consistent branding, status language and usable app shells | Existing app starters and identity |
| 2 | [passenger-trains.md](../_specs/passenger-trains.md) | Guest next trains, detail, languages and honest saved timetable | 1; timetable, running and changes APIs |
| 3 | [operations-workspace.md](../_specs/operations-workspace.md) | Corridor overview and preview/publish/withdraw flow | 1; Operations APIs |
| 4 | [station-boards.md](../_specs/station-boards.md) | Entrance and platform boards, including offline fallback | 1; same train/change contracts as 2–3 |
| 5 | [driver-journeys.md](../_specs/driver-journeys.md) | Assigned briefing, offline reading, report and acknowledgement | 1; identity, running, changes and reports APIs |
| 6 | [operations-reports.md](../_specs/operations-reports.md) | Control inbox, acknowledgement, incidents and auditor history | 3; reports/incidents APIs; pairs with 5 |
| 7 | [passenger-saved-journeys.md](../_specs/passenger-saved-journeys.md) | Saved commute, follow/unfollow and personal in-app changes | 2; ownership-protected API additions if missing |
| 8 | [passenger-food.md](../_specs/passenger-food.md) | Food tab, order, pending outcome, pickup code and status | 2; existing Grilled integration and local wiring |
| 9 | [operations-demo-controls.md](../_specs/operations-demo-controls.md) | Authorised simulation controls and dependency health | 3; Grilled backend T6 where required |
| 10 | [grilled-kiosk.md](../_specs/grilled-kiosk.md) | Provider staff mark Ready, Collected and No-show | Existing provider APIs; pairs with 8 |
| 11 | [pilot-app-acceptance.md](../_specs/pilot-app-acceptance.md) | Five connected journeys and reproducible demonstration evidence | Required feature slices above and backend recovery checks |

The linked specs now record the planning discussion and user-confirmed choices. Their unchecked tasks are not implemented or verified. This is a build order, not a requirement to finish every spec before writing code. After 1, contract discovery for the separate apps can proceed independently. Coordinate changes to shared tokens, backend contracts, identity and demo fixtures through their owning spec.

Steps 9–10 finish supporting parts of the existing pilot, rather than adding another SmartRail client. The Grilled staff page belongs to the provider. Azure deployment needs separate planning and is not silently included in app completion.

## Common context — every spec agent must read this

### Product and source of truth

- This is a learning project owned by one developer. Keep files discoverable and code explainable. Follow `AGENTS.md`, `.agents/skills/spec/SKILL.md` and `_specs/_template.md`; use the template only where useful.
- Read `docs/09-epics-features-stories.md`, `docs/08-prioritization.md` and the relevant sections of `docs/06-scope-decision.md`. Read the original `docs/final-assessment-scoped 2.md` for assessment context. Broad feature lists do not override the selected pilot scope.
- Read `docs/architecture/backend-decision.md`, `docs/architecture/identity-and-local-development.md` and the relevant existing backend specs. Their historical “what exists” paragraphs can be stale: inspect current source and tests before claiming a capability works.
- The corridor is Colombo–Kandy, with Gampaha → Colombo Fort as the guest opening route. Demo data currently includes fictional trains; check actual seeded stops, directions and dates before choosing a scenario. Do not assume the entire Kandy route or presentation-relative demo seeding already exists.
- Only schedules can come from checked public railway sources. Running information is **SmartRail simulation**. No GPS, real railway feed, movement authority, payments, background push, product AI or timetable editor enters these specs.
- The interactive station terminal remains design-only. `apps/station-web` delivers passive station boards, not an interactive search terminal or station staff management app.

### Existing code to preserve

| Area | Location and starting point |
| --- | --- |
| Passenger | `apps/passenger_app/`: Flutter, minimal guest/account screen and working identity plumbing |
| Driver | `apps/driver_app/`: separate Flutter app, minimal required-login/account screen |
| Operations | `apps/ops-web/`: React/TypeScript/Vite, Keycloak login and account/permissions screen |
| Station | `apps/station-web/`: React/TypeScript/Vite starter |
| Backend | `services/api/Modules/{Rail,Operations,Access,Catering}/`: one .NET modular monolith with PostgreSQL |
| Provider | `services/grilled/`: separate Node/TypeScript service and SQLite; contract in `openapi.yaml` |
| Local stack | `services/apphost/` and `services/README.md`: Aspire, PostgreSQL, Keycloak, API and Grilled |

Use the codebase-memory skill for structural exploration, including coverage checks and direct source fallback. This handoff is a starting inventory, not a substitute for checking the current branch. Preserve unrelated working changes.

Keycloak already owns sign-in. Reuse Authorization Code + PKCE, existing mobile secure token storage and web in-memory tokens. Public trains and station boards must work without Keycloak. Personal passenger actions and staff actions require an enabled local account; hiding a button does not replace server permission checks. Preserve the exact issuer/certificate setup documented for Android. Do not propose Flutter web/Windows as proof that native AppAuth works.

### Shared behaviour

- Use the existing server rules for ETA, confirmed changes and food eligibility. Keep schedule, observation, confirmed change and prediction distinct. Store IDs for **dated journeys and stop occurrences**, not just train numbers and station names.
- Below 180 seconds, an observation is current; from 180 to below 240 it is ageing; at 240 or with no evidence, its position and ETA are unknown. Read server thresholds and clock information; do not age data from the last HTTP response. Continue ageing between polls, after a 304 and when the app resumes. Never display an old estimate as “on time.”
- A separately confirmed platform change has its own time and remains visible when position becomes unknown. An early ending preserves served earlier stops and its final stop; later destinations are unserved, with no ETA or new food collection. Cancellation and early ending need different wording.
- Poll every 15 seconds for web boards/Operations and 30 seconds for mobile, with ETags where supported; refresh on resume/reconnect and after writes. Avoid overlapping requests, ignore superseded responses, and stop unnecessary background polling. SSE and SignalR are outside the required app work.
- Offline screens use honest labels and download/check times. Distinguish a stale train row from loss of the whole connection. An empty successful result is not an error. Plan initial loading, no saved data, partial failure, offline, reconnect, 401/403 and relevant 409 states.
- Branding should be consistent while layouts fit their users. Scope D15 calls for small shared token/status definitions, a simple CSS/Dart generator, `StatusBadge` and `FreshnessLine`. Do not create a large component library or copy an entire enterprise architecture into the clients.
- Passenger home/detail and station status wording need Sinhala, Tamil and English. Boards show all three together; no carousel. Staff apps use English for this pilot, with translation plumbing. Bundle appropriate fonts and allow long text. Generated translations require native-reader review; label unreviewed wording honestly.
- Include keyboard/screen-reader behaviour, adequate contrast and touch targets in the feature's checks. Driver screens use the documented light theme, large text and 56dp targets. Station boards have a clock, fixed simulation footer and no flashing/tickers.
- Prefer a few meaningful tests of real failure states. Reuse `tests/SmartRail.Api.Tests/Rail/freshness-vectors.json` in Dart and TypeScript. A mock or API-only test is not evidence that an actual mobile screen works.

### Known gaps to investigate explicitly

1. **Live next-train selection:** `RailEndpoints.cs` currently returns schedule-based candidates. Attaching ETAs to those rows does not prove a delayed train remains findable after its scheduled departure, or that a cancelled train cannot be recommended. Inspect this before choosing composition versus a small server read extension. Do not reproduce the ETA engine in Dart.
2. **Personal features:** Access declares saved-route, follow and alert permissions, but its inspected endpoints only implement account/grant management and `/api/me`. Verify current persistence and routes. Do not present local-only favourites as implemented account-owned subscriptions.
3. **Driver reads:** `/api/me` includes journey scopes, and report endpoints exist. A dedicated assigned-journey/briefing read was not found in the inspected route mappings. Determine whether existing contracts suffice; otherwise plan a small scoped read with backend enforcement.
4. **Attribution:** public changes currently include confirmation times but deliberately omit staff identity/audit details. Some scope prose asks who confirmed a change. Resolve the public source label and protected driver issuer needs explicitly; never expose the audit trail or invent a confirmer name to fill a UI label.
5. **Boards/overview:** do not assume a station-board or corridor aggregate endpoint exists. For this small pilot, evaluate bounded composition of existing reads before adding a specific server projection. Never hardcode sample train rows as the final data source.
6. **Demo controls:** current pause/resume routes use an internal service key; a `simulation.control` permission also exists. A browser must not receive that internal key. A protected staff route or server-mediated action needs planning. Grilled backend T6 is still unchecked in the inspected spec.
7. **Kiosk credentials:** provider kiosk APIs use collection-point-scoped credentials. Choose a small server-side authentication/session arrangement for a staff page; do not put provider or kiosk secrets in a JS bundle, URL or browser storage.

An app feature may need a small backend addition. Name it, assign it to the correct module and include an observable check in that feature's spec. Do not rewrite completed backend features or pretend all app contracts are already available. Coordinate any overlap with an existing backend task instead of duplicating it.

### What each resulting spec should contain

Use guided discussion, one consequential question at a time. Do not ask again about settled scope above. Distinguish recommendations from user-confirmed decisions. Consult an available SmartRail role agent only for a focused domain/design question that matters, following `AGENTS.md`; simulated advice is not real user validation.

Write one short `_specs/<feature>.md` with the user outcome, relevant current files, screen flow/states, a few decisions, necessary API changes and small runnable T1/T2 tasks with clear checks and dependencies. Usually 3–5 tasks will be enough, but do not force a quota. Mark missing prerequisites Blocked. Finish by explaining the first ready task and any unresolved choice. Do not implement code during spec generation.

## 1. App foundation — copy this prompt

```text
Use $spec to plan `_specs/app-foundation.md`.
First read `docs/app-spec-plan.md` through the end of Common context, then follow AGENTS.md.

Help me settle a minimal, consistent SmartRail visual language and usable shells for the four existing apps. Read scope D2, D13, D15 and the screen rules, inspect current themes/auth and look for existing approved designs before proposing a new style. Ask me about the most consequential unresolved visual choice; do not silently select a brand or add screens.

Include colour/type/spacing tokens and status meanings, a small CSS/Dart generation approach, StatusBadge/FreshnessLine, bundled language fonts and basic translation plumbing. Define simple navigation and permission-aware signed-in/signed-out/error states while preserving existing Keycloak integration. Passenger navigation must lead to public trains and a separate Food tab; do not gate it with login. Apply the foundation to a visible shell in each existing app, without building feature screens here.

Choose the smallest shared-code arrangement that fits the repository. No enterprise design-system package, general networking framework, state-management migration or abstraction solely for future reuse. Leave detailed freshness/cache behaviour to the consuming feature, with one agreed contract and reusable fixture source.

Checks should show consistent tokens/status meanings in React and Flutter, readable long Tamil/Sinhala text, useful accessibility checks, and working existing login/guest flows. Planning only; produce small runnable tasks, not application code.
```

## 2. Passenger trains — copy this prompt

```text
Use $spec to plan `_specs/passenger-trains.md`.
Read Common context in `docs/app-spec-plan.md`, the app-foundation spec if it exists, `docs/Features/01-passenger-app-features.md`, and the timetable/running/Operations backend specs. Inspect `apps/passenger_app/` and current Rail/public-change responses.

Plan a useful guest opening screen: Gampaha → Colombo Fort, changeable origin/destination, next two or three eligible trains, separate schedule/ETA, boarding platform and observation age. Prominently show cancellation, early ending and changes at the boarding stop. Recommend earliest arrival only when valid current estimates support it. Inspect the schedule-only next-trains limitation before deciding on a minimal backend extension.

Train detail shows stops, last known station and a simple route diagram. Keep a moving marker optional; remove it at unknown freshness. Preserve separately confirmed changes. Include Sinhala/Tamil/English home/detail wording, local public timetable persistence with age labels, cold-start offline/no-cache states, ETags, local ageing and reconnection. Public browsing must survive unavailable login services.

Keep saved commutes/follows and actual food ordering in their own specs; navigation can lead to those later. No GPS/map tiles, connection planner or tickets. Resolve source/issuer wording against the current public contract rather than exposing private staff data. Include actual Flutter screen checks for current → ageing → unknown → recovered and an honest offline timetable. Ask only about consequential choices not already settled. Planning only.
```

## 3. Operations workspace and changes — copy this prompt

```text
Use $spec to plan `_specs/operations-workspace.md`.
Read Common context in `docs/app-spec-plan.md`, app-foundation if present, `docs/Features/03-railway-operations-web-features.md`, `_specs/operations-changes.md` and the current Operations/Access code. Extend the existing React/Keycloak app at `apps/ops-web/`.

Plan a corridor table with train, last station, delay, status and observation age, then a focused journey workspace for composing DELAY, CANCEL, PLATFORM and EARLY_END. Show the server preview's before/after, served/unserved stops, affected screens and honest catering impact. Unknown order counts must not become zero or claim delivery. Confirm explicitly; show success only after commit. Allow withdrawal through the same preview/confirm flow.

Keep the exact reviewed draft and previewed whole-journey version together. Editing any preview input invalidates it. On 409, preserve useful input, reload current state and require a new review; never silently retry publication with a newer version. Plan safe recovery from an ambiguous write response by reading confirmed state before another write.

Respect permissions: Controller publishes operational changes; Station Staff only platform changes at their station. Auditor is read-only; SysAdmin has no operational write authority. Use current `/api/me` scopes and server enforcement, including direct URL and revoked-access cases. A change may link an existing incident where permitted; report/incident management is a separate spec. Demo health/controls are also separate. No timetable editor, account/grant UI or map dashboard. Include keyboard, error and two-controller conflict checks. Planning only.
```

## 4. Station boards — copy this prompt

```text
Use $spec to plan `_specs/station-boards.md`.
Read Common context in `docs/app-spec-plan.md`, app-foundation if present, `docs/Features/05-station-display-features.md` and scope's busy-station rules. Inspect `apps/station-web/` and current public timetable/running/change reads.

Plan one passive React app with entrance and platform views, configured by station and platform without passenger login. Entrance shows upcoming arrivals/departures; platform shows its next train. Waiting-area use reuses entrance. Prioritise scheduled time, destination, platform, expected time and a short status/reason supported by data. A changed platform must be clear at both the old and new platform as appropriate; do not silently remove a train before a waiting passenger can understand the move.

Show Sinhala, Tamil and English together, a clock and a fixed simulation footer. No carousel, flashing or free-text notice composer. Distinguish a single train's stale position/ETA from whole-feed failure. Persist enough public timetable to reload offline; show Live updates unavailable and timetable-only rows, with no old ETA or platform override presented as current. Recover automatically without resetting evidence age. Include first-ever boot without cache and Colombo date rollover.

Evaluate small existing-read composition before adding a board projection. Record any attribution/reason field gap honestly. Test observation threshold boundaries/304 ageing, platform/early-ending behaviour, offline reload/reconnect, long language text and legibility at the intended screen size/distance. This is not the interactive station terminal. Planning only.
```

## 5. Driver journeys and reports — copy this prompt

```text
Use $spec to plan `_specs/driver-journeys.md`.
Read Common context in `docs/app-spec-plan.md`, app-foundation if present, `docs/Features/02-driver-operator-app-features.md`, `_specs/identity-and-permissions.md` and `_specs/driver-reports.md`. Inspect the separate Flutter driver app and Access journey scopes/report endpoints.

Plan required login, an assigned-journey list and a briefing showing stops, times and confirmed changes with issuer/time. Check whether existing reads support this; include the smallest protected read if not. Save the last downloaded briefing per account/journey for offline reading, with its download time and old-data warning. Define cold-start session expiry, offline reopening, account switching/logout cache isolation, and revoked assignment on reconnection. Offline cache never grants authority to write.

Add a short stopped-use reason report with the backend's existing codes. Keep a stable clientReportId through timeout/retry/restart and use own-report lookup to reconcile uncertainty. Distinguish Not sent / no confirmed receipt, Sent — awaiting Control acknowledgement, and Acknowledged by [name] at [time]. Server receipt is not human acknowledgement. Do not claim a timed-out request was certainly never received.

Default scope excludes automatic offline sending; it is optional and requires explicit agreement. Unsent drafts and manual recovery are enough. Urgent issues direct the driver to established radio/emergency procedures and never wait in an automatic queue. No movement/speed orders, driver tracking, chat or defect photos.

Use the documented light theme/large targets. Include real device sign-in/briefing/offline checks and focused tests for retry without duplicate reports, acknowledgement updates and permission loss. Planning only; ask about unresolved behaviour before finalising.
```

## 6. Control reports, incidents and audit — copy this prompt

```text
Use $spec to plan `_specs/operations-reports.md`.
Read Common context in `docs/app-spec-plan.md`, operations-workspace if present, `_specs/driver-reports.md`, and the current report/incident/audit endpoints. This extends `apps/ops-web/`; no new staff app.

Plan the paginated Control inbox, report detail and explicit acknowledgement with controller name/time. Opening a report or incident does not acknowledge it. Support simple Open → Resolved incidents, optionally opened from a report, and selecting an open incident in the existing change composer. Incident resolution does not withdraw a published change or acknowledge its report.

Include the Auditor's permitted incident/journey history with actor/action/time and a clear read-only layout. Preserve the distinction that a Controller does not automatically have the Auditor's audit.read permission. Handle duplicate clicks, another controller winning first, cursor refresh and an incident resolving while a change draft is open.

Checks follow the real loop: driver submission → Control receipt → explicit acknowledgement → driver sees named acknowledgement → linked published delay → incident resolution leaves delay active → Auditor reads history. No formal railway escalation classifications, auto-publish or analytics. Planning only; keep tasks based on working screens.
```

## 7. Passenger saved journeys and in-app alerts — copy this prompt

```text
Use $spec to plan `_specs/passenger-saved-journeys.md`.
Read Common context in `docs/app-spec-plan.md`, passenger-trains if present, the accounts/passengers ERD and Access permissions/current endpoints. Do not assume the tables, follow endpoints or personal feed already exist.

Plan saving a commute, following/unfollowing a dated journey and seeing relevant personal service changes while the app is open. Prompt Keycloak sign-in only at the personal action, preserve that action's route/journey context, and let the user return to guest trains. A saved route is a reusable origin/destination pair; a follow targets a dated journey, not every train with that number.

Include only the smallest missing ownership-protected persistence/API in Access. Reuse confirmed changes as facts; do not add a notification service/broker or fabricate an alert table if a simple projection suffices. Discuss alert lifetime/read-state only if it changes the user outcome. Handle reconnect/poll deduplication, withdrawal/new versions, early ending and repeated identical responses. Public route notices remain available without a follow.

Cache personal saved information separately per account, protect pickup codes/tokens from unrelated caches, and clear/isolate it on sign-out/account switch. No push, email, SMS, registration or journey planner. Checks prove another passenger cannot read/change a saved route/follow and an unfollowed journey produces no further personal alerts. Planning only; identify backend work honestly and ask about any unresolved product choice.
```

## 8. Passenger food — copy this prompt

```text
Use $spec to plan `_specs/passenger-food.md`.
Read Common context in `docs/app-spec-plan.md`, passenger-trains if present, `_specs/grilled.md`, `services/grilled/openapi.yaml` and the current SmartRail Catering request/response types. Extend `apps/passenger_app/`; passenger calls go only to SmartRail.

Plan a separate Food tab: select the dated journey and an eligible future served stop, browse provider menu/prices in LKR, choose quantities, review, sign in when ordering, place an order, and show pickup code/current status. Pay at collection. Read the real contract for field names and stable refusal reasons; do not copy provider endpoints directly into the mobile app.

Keep one client request key for the same submission through timeouts, app restart and repeated taps. A 202 is pending: retain/poll the stable order link and say the outcome is not yet known. Never mint a second request just because the response was lost. Handle final refusal, stock/cutoff changes, provider outage, stale train eligibility, updated pickup time, cancellation reasons, Ready/Collected/No-show and server-approved cancellation before cutoff.

Do not cache or hardcode provider cutoff as an authority. Unknown train status blocks new orders but does not automatically cancel an existing one; early ending beyond pickup does. A withdrawal does not revive a cancelled order. No edits, online payments, refunds or ratings. Keep personal order details isolated from guest caches. Include screen checks for pending recovery, one order after retry, changed cutoff/ETA and terminal status after provider callbacks. Planning only; record any Grilled T6 blocker without marking it complete.
```

## 9. Operations demo controls and health — copy this prompt

```text
Use $spec to plan `_specs/operations-demo-controls.md`.
Read Common context in `docs/app-spec-plan.md`, operations-workspace if present, `_specs/simulated-running.md`, `_specs/grilled.md` T6, `services/README.md`, SimulatorSetup and Access's simulation.control permission.

Plan a small authorised panel in the Operations app to pause/resume a simulated journey and observe its recovery. Show simulation state separately from the train's evidence freshness. Add useful API/provider dependency and pending-delivery/backlog information, with failed/unknown states represented honestly; do not rebuild the Aspire dashboard. Link local traces where useful.

Internal simulator/provider credentials must stay server-side. Plan the smallest authenticated staff route/adapter and enforce the correct role/permission/environment on the server; never make a secret a VITE variable. Resolve any permission gap for health/provider controls explicitly rather than granting every staff member access. Provider outage/restore must remain reachable during the simulated failure and must not stop public train reads.

Coordinate with existing Grilled T6: keep service wiring, diagnostics and recovery proof owned there where applicable; this spec owns their staff-facing controls. Do not create duplicate workers or tests to replace its unfinished drill. Inspect whether repeatable presentation-relative seeding exists; record the minimal needed fixture setup, not a timetable editor or browser clock override. Checks show denied unauthorised actions and real pause → unknown → resume plus provider outage → backlog → recovery. Planning only.
```

## 10. Grilled kiosk staff page — copy this prompt

```text
Use $spec to plan `_specs/grilled-kiosk.md`.
Read Common context in `docs/app-spec-plan.md`, `_specs/grilled.md`, provider OpenAPI, `services/grilled/src/kiosk.ts` and its auth/config/server code. This is a small provider-owned staff page, not the interactive station terminal or a fifth SmartRail app.

Plan viewing one authorised collection point's orders/items/pickup times and marking Ready, Collected with the customer's matching code, or No-show when SmartRail proves the train passed. Show train-driven pickup changes and cancellations clearly. Do not pre-display the secret matching code as if it verifies the customer's possession; inspect the existing kiosk response and use the server check.

First settle a minimal server-side staff login/session arrangement that keeps kiosk credentials out of browser code/storage and enforces collection-point scope. Prefer serving the small page from the provider if it fits; avoid another deployable or reusing the SmartRail service secret. Plan relevant CSRF/session handling if cookies are chosen. Do not invent a large provider identity system.

Handle invalid codes, disallowed/terminal transitions, concurrent staff actions, unknown passage and unavailable SmartRail. A button cannot make a scheduled departure into proof of passage. Checks use a real provider and show Ready/Collected reaching the passenger through callbacks, and a different kiosk being denied. No menu editor, inventory dashboard or payment UI. Planning only; include necessary small backend work transparently.
```

## 11. Connected pilot acceptance — copy this prompt

```text
Use $spec to plan `_specs/pilot-app-acceptance.md`.
Read Common context in `docs/app-spec-plan.md`, the implemented client specs, the five journeys in `docs/06-scope-decision.md` under How we will prove it works, and current test/CI/demo setup. This spec closes observed integration gaps and records evidence; it does not add new product features.

Plan the existing five connected journeys: (1) a delay reaches guest passenger reads, driver briefing and station board with truthful attribution/time; (2) ageing/unknown, marker removal, offline board and recovery; (3) food delay → pickup code collection → callback status; (4) cancellation and early ending cancel only affected open orders and preserve served stops; (5) report → explicit acknowledgement → linked incident/change → resolution → auditor history. Check the current final-stop/collection-point fixture: do not choose an endpoint that makes early ending impossible to demonstrate.

Use Playwright for web/system flows and real API calls where a mobile step is represented by its API. Add one actual Flutter integration flow against local Aspire plus relevant widget tests; label API-only coverage honestly. Use controllable clocks for automated thresholds, with a separate short real-stack rehearsal. Reuse rule/backend tests and shared freshness fixtures instead of multiplying infrastructure.

Also prove the two-editor 409, station/journey/account boundaries, logout/cache isolation, an ambiguous order retry, unavailable Keycloak for guest reads, and provider recovery with durable data. Include focused accessibility checks and actual Sinhala/Tamil reader review as available; do not claim review without people/evidence. Define reproducible demo seeding/configuration and record commands, results, defects and specific blockers; skipped dependency tests are not passing checks.

Conclude with separate statements for local app readiness, unresolved defects and Azure deployment. The current Azure plan/Keycloak/provider storage has separate work; do not call the whole system production-ready from a local test pack. Planning only, short runnable tasks, first ready check identified.
```
