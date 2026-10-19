# Driver hi-fi screens

## Outcome

Complete the SmartRail driver journeys in the existing Figma file, with the 2560 × 1600 cab tablet as the primary surface and phone companions for pre-duty use. The tablet designs were uniformly scaled 2× from their original 1280 × 800 compositions at the user's request. Reuse the HiFi Color Light/Dark tokens and Noto Sans typography. Present light screens on dark section backgrounds and dark screens on light backgrounds, as requested for Passenger.

## Decisions

- User confirmed on 18 October: follow the current pilot report contract — Train fault, Signal delay, Obstruction, Cannot continue; no free-text note.
- User requested tablet priority and filling missing screens. Implementation assumption: light is the default, with dark counterparts and the existing phone flows retained.
- Follow [driver scope](../docs/Features/02-driver-operator-app-features.md), [driver reports](driver-reports.md), and [identity boundary](../docs/architecture/identity-and-local-development.md). Staff sign-in continues through the identity provider; SmartRail does not collect credentials or introduce a PIN.
- Sunil's advice (SmartRail conversation `cnv_ffc6b06536f94101944e`) informed sign-in, no assignment, report history, explicit Not sent retry, empty lists and session expiry. Do not adopt his suggested PIN, automatic shift timeout policy, read/unread workflow, or speed-restriction examples: these are outside the current contract.
- Preserve delivery versus acknowledgement: Not sent — Control has not been told; Sent — awaiting Control acknowledgement; Acknowledged by Dilani at 6:55 only after a controller action. No offline queue or automatic resend. Reports are made while stopped; urgent issues use the radio.

## Screen inventory

Tablet (17 per theme): staff sign-in; today's duty; no assignment; Updates list; platform-change detail; delay detail; Updates empty; report composer; Not sent; Sent awaiting Control; Acknowledged; Reports list; Reports empty; offline saved briefing; offline without saved copy; session expired; staff account.

Phone (10 per theme): D01 sign-in, D02 assigned briefing, D03 update detail, D04 report composer, D05 Not sent / Sent / Acknowledged, D06 saved offline briefing, D07 no assignment, D09 staff account. The optional D08 queue is omitted.

Tablet targets: body 20, times at least 28, 96px main actions, generous navigation and report choices. Phone design uses the same readable hierarchy at 3x scale and at least 168px action targets. Scheduled and expected times, observation age and named Control updates remain distinct. Session expiry locks the protected view without inventing a timeout policy.

## Tasks

- [x] **T1 — Complete tablet light flow.** Create shared driver components and the 17 tablet frames on `HiFi · Driver screens` in [SmartRail UI](https://www.figma.com/design/38B21GKxgb3wxRar06gHXP/SmartRail-UI). Check: every main navigation destination and each report outcome exists, text and controls fit 1280 × 800, and the four report codes match the pilot.
- [x] **T2 — Complete phone companions.** Build the 10 phone states with the same information and permissions. Depends on T1. Check: no fixed action overlaps content and source wireframe flows are covered without the optional queue.
- [x] **T3 — Dark counterparts and flow validation.** Clone the verified light set into the Dark mode, remap prototype destinations, and inspect all screen families. Depends on T1/T2. Check: 54 screens, editable layers and linked components, correct modes, no orphaned prototype destination, and readable status contrast.

## Boundaries

Figma design only, no app/backend implementation. No roster, duty acceptance, leave, fatigue, crew/loco records, photos, chat, GPS/speed/signal advice, driving authority, background send, or operational acknowledgement from a driver. No claims of official railway procedure or user testing. The existing wireframe future-ideas board remains separate.

## Completed design and checks — 18 October 2026

[Open the driver duty screen in Figma](https://www.figma.com/design/38B21GKxgb3wxRar06gHXP/SmartRail-UI?node-id=178-73). Page: `HiFi · Driver screens` (`174:36`).

- Created 54 editable screens: 17 tablet and 10 phone states in each theme, arranged in 10 named flow sections. Light screens have explicit Light mode on dark section backgrounds; Dark screens have explicit Dark mode on light backgrounds.
- Reused shared driver buttons, navigation, reasons, status chips, changes, report rows and recovery panels; added phone button/reason variants and the 3x phone text ramp. All 508 component instances retain their main component.
- Linked 26 prototype starting points and the main navigation, report receipts, recovery and account actions. Destination checks found no unknown target, cross-theme jump or cross-device jump.
- Checked 1,056 text layers: no text falls outside a screen, all text paints use semantic variables, and minimum measured text/background contrast is 4.57:1. Added success/danger ink tokens to strengthen small Light-mode status text without changing the passenger screens.
- All tablet content fits within 800px. Phone content clears fixed actions by at least 16px. Visually inspected the main light/dark duty and report families, saved/offline states, report history, identity recovery and update detail; corrected countdown clipping, icon foregrounds and phone spacing.
- Verified exactly four reasons on all four report composers, with no note input. Prototype status refresh illustrates acknowledgement by Dilani; it does not give the driver a Control acknowledgement action. Form choices are static design states rather than a working submission backend.

No app/backend code changed. These are design and prototype checks, not user testing.

## Tablet size correction — 18 October 2026

- [x] **T4 — Scale all tablet designs to 2560 × 1600.** All 34 tablet screens (17 Light and 17 Dark) are uniformly scaled 2× from 1280 × 800. Original layout proportions, fonts, icons, spacing and controls use the same factor. The earlier tablet measurements above describe the original 1× compositions: body 20 becomes 40, time 28 becomes 56 and 96px actions become 192px. Phone dimensions remain unchanged.

Rearranged the enlarged flow sections to avoid canvas overlap. Screen IDs, themes, component connections and prototype destinations remain intact. Checked all 34 persisted frame dimensions, 728 text layers and 410 component instances: no screen overflow or missing font/component. Restored source instance text sizing to prevent label wrapping introduced by fixed text-box overrides; visually checked Light duty and Dark reporting after correction.

## Logo update — 19 October 2026

Replaced the shared brand mark in all 42 driver screen placements that display it with the supplied transparent `driver-logo` PNG, using a reusable `Driver / App logo` component in `HiFi · Components`. The logo has no backing fill or card. The 12 phone states with title/back headers keep their existing header pattern. Checked that no old brand-mark instances remain on the Driver screens page, each logo and its image retain the same proportions, and tablet navigation and phone headers render clearly in Light and Dark.
