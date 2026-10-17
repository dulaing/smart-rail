# Driver reports and Control acknowledgement

## Why

Drivers need to know whether Control received a report and whether a person acknowledged it. Controllers need to record an internal incident without accidentally publishing or clearing a passenger-facing service change.

## Done when

An assigned driver submits a short reason and can read server receipt separately from a named controller's explicit acknowledgement. Control can read reports, open an optional incident, publish related changes through the existing preview/confirm flow, and resolve the incident. Retries and concurrent controller actions preserve the first recorded result. An auditor can inspect incidents and the history; another driver and a SysAdmin cannot perform operational actions.

## Scope and decisions

- **User confirmed:** backend flow only, covering Epic 3 reporting and Epic 2 acknowledgement/incidents in the [epics](../docs/09-epics-features-stories.md). Driver and Control screens, saved briefings and the optional offline queue are separate features.
- Follow the [backend boundaries](../docs/architecture/backend-decision.md#code-boundaries), [Operations ERD](../docs/architecture/erd/02-operations.mmd) and [data-model rules](../docs/architecture/data-model.md#tables-and-rules-not-fully-visible-in-the-diagrams). Use feature folders under `services/api/Modules/Operations/`, the existing EF context and `changes` schema. Reuse named Rail journey reads and Access authorization/auditing; add a small Access name-lookup interface if needed rather than querying its account sets from Operations.
- Follow the [driver scope and demonstration](../docs/06-scope-decision.md): `Not sent — Control has not been told` exists only on a device. A committed server report is `SENT_AWAITING_CONTROL`; only an explicit acknowledgement makes it `ACKNOWLEDGED`, with controller ID, display name and UTC time. Opening or resolving an incident is not acknowledgement. No endpoint accepts a client-selected actor, receipt time or acknowledgement time.
- Acknowledgement, incident creation/resolution and change publication are separate actions. None automatically performs another. Dilani's advice (conversation `cnv_9e4ab6e6fb614dd0afd6`) informed this separation and keeping changes active after incident resolution. Her suggestion to require an acknowledged incident for every service change is **not adopted**: the architecture allows optional incidents and existing independent change publication must keep working. No claim of official SLR procedure is made.

### Submission, reads and permissions

- Implementation defaults: accept a UUID `clientReportId` and a `reasonCode` from `TRAIN_FAULT`, `SIGNAL_DELAY`, `OBSTRUCTION`, `CANNOT_CONTINUE`. These are pilot labels, not a formal incident classification. Reject missing/empty IDs and unknown codes with 400; no free text, photos, chat or severity workflow.
- A new report requires `driver.reports.create` for the route's journey. Resolve the submitter from the token's local account and check assignment, active status and unexpired permission on every request. Use Rail's existing interface to reject a missing journey with 404. The reason does not change observations, ETA, journey versions or public service state.
- Enforce global uniqueness of `client_report_id` in PostgreSQL. A new commit returns 201. A retry by the same driver with the same journey/reason returns 200 with the existing report's **current** acknowledgement state and original receipt time, even after acknowledgement. Key reuse with different input returns generic 409 without another user's report data. Check authorization before replay lookup; concurrent identical submissions create one row and one successful submission audit.
- Add `driver.reports.read` with Journey scope to the seeded Driver role, alongside an explicit submitter ownership filter. A driver can list/read only their own reports on currently assigned journeys, including lookup by client key after a lost response. A different driver's report returns 404; an unassigned journey returns 403. Losing a grant or assignment removes access on the next request.
- Controllers use `incidents.manage` to list/read and acknowledge reports and create/resolve incidents. Auditors use `incidents.read` for incident reads and `audit.read` for history; this does not grant the raw report inbox. Other roles have no operational access unless explicitly granted these permissions. Missing/invalid tokens return 401; authenticated callers without permission return 403. Reuse Access's denied-action auditing.
- Report reads return ID, client key (own read only), journey ID, reason, submitter ID/name for Control, server receipt time, state and nullable acknowledgement ID/name/time. Driver responses omit other staff details. Incident reads return ID, source report ID, reason, state, opener and open/resolution times, plus linked change IDs. Keep all of this out of anonymous train APIs.
- Lists use a stable `(time, id)` order, an opaque continuation cursor and a default/max page size of 50/100. Control's report inbox is newest first with optional journey and acknowledged/unacknowledged filters; a driver's list is newest first within a journey. Incident lists support Open/Resolved filtering. Ordinary HTTP polling is sufficient; no SSE dependency.

### Acknowledgements, incidents and history

- Store `acknowledged_by_id` and `acknowledged_at` together, both null before acknowledgement. Use a conditional update so the first controller wins. Repeating or concurrently acknowledging returns 200 with the winning name/time, without overwriting it or creating another successful acknowledgement audit.
- Incidents have only `OPEN` and `RESOLVED` states. Creation accepts an optional source report ID; report-backed incidents copy its reason and enforce unique `source_report_id`. Standalone incidents require a valid reason code. Creating a report-backed incident again returns the existing incident (200 rather than 201), including if it is already resolved. No implicit acknowledgement is required or performed.
- Resolve with a conditional Open-to-Resolved update and server time. Repeats return the existing resolved record without changing its time or duplicating successful audit entries. No reopen, edit, delete, assignment or escalation workflow. Incident resolution never changes a report acknowledgement, journey version, published change, ETA or outbox.
- Add nullable `incident_id` to service changes and optional `incidentId` to publish requests/previews. A report-backed incident can explain changes only on its report's journey; reject unknown incidents with 404 and wrong-journey or resolved incidents on a new publish with 400. A standalone open incident may explain changes on multiple journeys. One incident may explain many changes. Existing requests without an incident remain valid.
- Preview shows the link; confirm revalidates it. Serialize linked publication against incident resolution in the transaction so a new change cannot attach after resolution wins. Retain the existing whole-journey version check and atomic change/audit/outbox commit. Withdrawals retain the original change's incident link, even if now resolved, and cannot substitute another incident. Existing historical links remain readable after resolution. Do not expose internal incident IDs in public change responses.
- Commit each new report, first acknowledgement and incident transition with its successful Access audit row in one transaction; audit failure rolls the action back. Record actor, action, target, time and correlation ID without request bodies or secrets. Report-backed actions also appear in the existing protected journey audit; standalone incidents need a protected incident audit read. Keep enough target information to follow report → incident → changes without duplicating success records on retries.

## Proposed API

| Route | Purpose |
| --- | --- |
| `POST /api/driver/journeys/{journeyId}/reports` | Submit `{ clientReportId, reasonCode }`; 201 new, 200 identical retry. |
| `GET /api/driver/journeys/{journeyId}/reports` | Own reports; optional `clientReportId` filter for response-loss recovery. |
| `GET /api/driver/journeys/{journeyId}/reports/{reportId}` | Own report and current acknowledgement. |
| `GET /api/operations/reports` and `GET /api/operations/reports/{reportId}` | Control inbox/detail. |
| `POST /api/operations/reports/{reportId}/acknowledge` | Explicit acknowledgement; empty body. |
| `POST /api/operations/incidents` | `{ sourceReportId }` or `{ reasonCode }`, exactly one; 201 new, 200 existing source link. |
| `GET /api/operations/incidents` and `GET /api/operations/incidents/{incidentId}` | Controller/Auditor list and detail. |
| `POST /api/operations/incidents/{incidentId}/resolve` | Resolve; empty body, 200 including repeats. |
| `GET /api/operations/incidents/{incidentId}/audit` | Audit-read permission; bounded chronological history using the existing audit contract. |

Reuse the existing change preview/confirm routes for the optional incident link. All new routes require authentication and explicit local permission checks. Return 404 for missing authorized targets and Problem responses for validation/conflict failures. Successful mutations return the current resource.

## What exists now

- `Modules/Access/PilotRoles.cs`, `AccessGuard.cs`, `CurrentAccount.cs` and `AuditTrail.cs` provide journey-scoped report-create permission, network incident permissions, account/grant checks, local actor identity and transactional audit staging. The new report-read permission and staff name lookup still need wiring.
- `Modules/Operations/Changes/` implements preview, atomic publish/withdraw, version conflicts and protected journey audit. `ChangeDtos.cs`, `ChangeTypes.cs` and `OperationsModel.cs` currently have no incident link. `OperationsSetup.cs` wires the existing slices. [Operations changes](operations-changes.md) records completed checks; its earlier “What exists now” paragraph is historical.
- Reports and incidents are not implemented in the inspected Operations source. Reuse `tests/SmartRail.Api.Tests/` and its existing disposable PostgreSQL API fixture; database checks use `SMARTRAIL_TEST_POSTGRES`. No backend or client implementation is part of this spec-writing request.

## Tasks

- [x] **T1 - Submit and recover one report safely.** Add the report model/mapping/migration and submit/own-read endpoints under `Modules/Operations/Reports/`; wire the existing context/setup, new Access read permission and name lookup. Check: `dotnet test tests/SmartRail.Api.Tests` with disposable PostgreSQL proves migrate/reseed safety, assigned-driver receipt, own/key recovery, concurrent identical retries, conflicting keys, 400/401/403/404 cases, grant/assignment removal and atomic submission audit rollback. A retry after later acknowledgement must return the latest state (covered again in T2). *Done: migration `AddDriverReports`; `Modules/Operations/Reports/` (entity, mapping, store, `ReportSubmitter`, `DriverReportEndpoints`, opaque `(time, id)` cursor); `driver.reports.read` on the seeded Driver role; `IAccountNames` as the Access name lookup; `IAuditTrail.ReadForTargetsAsync` so the journey audit also lists the journey's reports (`report.submit` is audited against the `REPORT` target). `client_report_id` is stored as a `uuid`, not a string as the ERD draws it. Verified with `SMARTRAIL_TEST_POSTGRES` on a disposable `postgres:18.3`: 321 of 321 tests in the project.*
- [ ] **T2 - Let Control acknowledge explicitly.** Depends on T1. Add the bounded inbox, report detail and acknowledgement endpoints plus acknowledgement constraints. Check: PostgreSQL API tests distinguish receipt from acknowledgement, expose the named winner/time to the owning driver, race two controllers, replay a lost acknowledgement response and a submission after acknowledgement, verify pagination and role isolation, and inject audit failure to prove rollback.
- [ ] **T3 - Open, inspect and resolve incidents.** Depends on T2. Add incident persistence/migration, bounded list/detail, create/resolve and incident audit reads under `Modules/Operations/Incidents/`. Check: PostgreSQL API tests cover optional source reports, concurrent duplicate creation/resolution, immutable winning times, separate acknowledgement, controller/auditor permissions, readable history, rollback on audit failure and no change to public journey state.
- [ ] **T4 - Explain a service change with an incident.** Depends on T3. Extend the existing change request, preview, entity/mapping and confirm path with the optional link; update API/setup documentation in `services/README.md`. Check: database tests publish multiple linked changes, reject invalid links, race resolution against linked publication, preserve links on withdrawal after resolution and keep unlinked publication valid. Re-run existing Operations version-conflict, rollback, public response and module-boundary tests. Then run the feature acceptance drill below.

Database checks require a disposable PostgreSQL connection. If unavailable during implementation, record the concrete blocker here; skipped database tests do not complete a task. T1's check has passed; the later tasks' checks have not been run.

## Feature acceptance drill

Using the real API and seeded roles: submit as an assigned driver, simulate a lost response and retry the same key, observe awaiting-Control state, acknowledge as Control and read the named acknowledgement as the driver, create an incident, preview/publish a linked delay, resolve the incident and prove the delay remains, then explicitly preview/withdraw it. Read the history as Auditor and demonstrate denied operational writes by SysAdmin and an unassigned driver. Report API evidence separately from any future screen demonstration.

## Boundaries

No driver/Control screens, client report drafts, saved briefing, offline queue, automatic resend or background notifications. Future clients must preserve the distinction between no confirmed receipt and server receipt; a backend test cannot prove the device's Not sent display. Urgent matters use established radio/emergency procedures; future client screens must say so and support use while stopped. No safety-critical orders, real railway feeds, automatic incident creation, automatic public changes or RailBites integration beyond the existing service-change outbox behavior.
