# Operations web hi-fi

## Outcome

Complete editable desktop Operations screens in SmartRail-UI, following the Operations wireframes and current pilot contracts. Use 1440 × 900 frames, the existing HiFi Color themes, Noto Sans, and linked shared components. Light screens sit on dark section backgrounds; dark screens sit on light backgrounds.

## Decisions

- User confirmed 18 October: include Light and Dark designs, and SimOperator owns all demo controls and diagnostics. This is a Figma design pass; it does not change the current light-only implementation scope.
- Follow [workspace](operations-workspace.md), [service changes](operations-changes.md), [reports/incidents](operations-reports.md), [demo controls](operations-demo-controls.md) and [identity](identity-and-permissions.md). These contracts supersede older wireframe fields and delivery claims.
- Staff sign-in hands off to the identity provider. Controller cannot inspect protected audit or demo diagnostics without their permissions. StationStaff can change only platforms at its granted station. Auditor has read-only incident and paged journey/incident history. SysAdmin has no operational write authority.
- Review all four change kinds and explicit withdrawal. Whole-journey version conflicts invalidate review and retain useful input. A lost write response stays uncertain until reconciled; no automatic replay. Confirmation records the committed change; provider delivery is a separate state. Do not invent acknowledgements from passenger apps, station boards or drivers.
- Driver reports use four reason codes and no note. Reading, acknowledgement, incident creation, resolution and service changes are separate actions. No incident note/reopen workflow is added.
- Dilani's advice (conversation `cnv_5114c22801534507b570`) informed stale-review priority, distinct read failures and explicit resolution confirmation. Do not adopt an invented 90-second preview expiry or portray failed loading as no report received.
- Use seeded pilot-corridor examples, not the older Kandy expansion. Keep unknown food counts and observation failures explicit. Simulation pause is separate from observation age; no real train controls.

## Tasks

- [x] **T1 — Complete the Light desktop flow.** Build reusable web actions, navigation, fields and data presentation; cover corridor/access states, four composers and reviews, commit/delivery, withdrawal and recovery, report/incident handling, role-specific/audit views and demo health/control states. Check layouts fit 1440 × 900, contract and role boundaries are accurate, and main actions link to concrete states.
- [x] **T2 — Complete Dark counterparts.** Depends on T1. Reuse semantic tokens and components, preserve opposing section backgrounds, and remap prototype destinations within theme. Check all Light states have Dark matches.
- [x] **T3 — Validate and record the design.** Depends on T1/T2. Check screen inventory, editable text/component links, text bounds and contrast, report reasons, unknown versus empty state, and prototype targets. Visually inspect each flow family and repair scoped defects. Record actual checks and Figma links here.

## Boundaries

Design/prototype only. No app/backend changes, timetable or grant editor, analytics, operational classifications, notes/chat, offline writes, automatic publication/acknowledgement or train movement authority. Static form variants illustrate flows; they are not a working backend.

## Completed design — 18 October 2026

[Open the Operations corridor in Figma](https://www.figma.com/design/38B21GKxgb3wxRar06gHXP/SmartRail-UI?node-id=202-66). Page: `HiFi · Operations web` (`199:36`).

Created **106 editable desktop screens: 53 Light and 53 Dark**, in 12 paired flow sections. Each theme contains:

- Access/corridor: 7 states — sign-in, overview, journey, loading, empty, partial failure, connection loss.
- Compose/review: 8 states — four relevant-field forms and four impact previews.
- Publication/recovery: 10 states — four matching publication receipts, separate provider delivery, stale review, uncertain outcome, withdrawal review/result and invalid incident link.
- Reports/incidents: 11 states — inbox, report detail, acknowledged receipt, uncertain acknowledgement, empty/error inbox, incident list/create/open, resolution confirmation/result.
- Roles/audit/account: 11 states — journey audit and final page, incident audit, read-only incident, station overview/platform draft/review/result, denied access, expired session and account.
- Demo/diagnostics: 6 states — healthy, degraded and recovering provider states, running and paused feed states, uncertain control response.

### Checks actually run

- All 106 roots are 1440 × 900 with the correct explicit theme. All outer sections use the opposing theme and no placeholder shimmer remains.
- All 1,966 component instances retain a main component. All 3,572 visible text layers use bound semantic colour paints; no missing fonts, text outside screen bounds, or content below the 24px lower margin. Maximum body bottom is 862px.
- Measured minimum text/background contrast: **4.58:1**; no text below 4.5:1 in the audit.
- Linked 44 main/recovery prototype starting points. Destination checks found no unknown destination, cross-theme jump or cross-role link beyond the shared sign-in handoff.
- Visually inspected the light corridor, journey, composer, early-ending review, conflict, report acknowledgement, incident-resolution confirmation, scoped station review and degraded provider states. Inspected dark corridor, early-ending review, report acknowledgement, audit, station review and provider recovery. Fixed filter alignment, the selected corridor row, crowded diagnostic spacing and technical draft copy.
- Four report reasons remain consistent with the pilot, with no note field. Incident resolution leaves acknowledgement and active service changes unchanged. Provider recovery retains failed work and does not declare an empty backlog on restore.

The initial inventory grew from 46 to 53 per theme to include separate publication receipts, the full scoped station flow, provider recovery and the audit end page. These are static design fixtures with linked main actions, not a connected application or backend test. No app/backend implementation, build or automated browser test was run for this Figma-only task.
