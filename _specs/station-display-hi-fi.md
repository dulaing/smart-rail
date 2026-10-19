# Station display hi-fi

## Outcome

Complete editable 1920 × 1080 station-display designs in the existing SmartRail-UI Figma file. Match the existing HiFi Color tokens, brand and Noto Sans/Tamil plus available Sinhala foundation typeface. User explicitly confirmed Light only on 18 October. Create one Light set, on dark canvas sections for contrast; remove the initial Dark staging sections.

## Decisions and sources

- Follow [station boards](station-boards.md), [display features](../docs/Features/05-station-display-features.md), [busy-station rules](../docs/06-scope-decision.md#design-work-and-screen-rules) and the six Station Displays wireframes. The current contract overrides older wireframe claims.
- Passive boards have no search, login, theme picker, touch controls, carousel, flashing or ticker. Waiting areas reuse the entrance composition with a short confirmed-delay explanation.
- Scheduled and expected times stay separate. Cancellation and unserved stops are notices, not operating departures. Keep earlier served stops and the new final stop valid. Distinguish origin departures and final arrivals.
- Show a platform move at both original and effective platforms, retained while relevant. Withdrawal/fresh departure examples clear the move only on authoritative evidence. At UNKNOWN, hide the ETA but retain an independently confirmed platform and timestamp.
- Whole-feed offline mode has one prominent trilingual banner and timetable-only rows, without cached effective platforms or ETAs. Include no-cache, partial-read failure, reconnecting/recovered, overnight/expired-date, loading, empty and invalid configuration states.
- Show English, Sinhala and Tamil simultaneously for station names, statuses and notices. Public attribution is translated Staff-confirmed change · time, with no controller identity or protected incident cause. Reuse shared/design/status.json where available. Other short wording and station names reuse wireframes or are design drafts.
- Sinhala and Tamil strings in the shared source are explicitly unreviewed. Physical four-metre/sunlight checks and native-reader review remain pending; a Figma contrast/layout check does not complete those checks.
- The SmartRail agent CLI failed to resolve its launcher, and an equivalent Python entry-point call did not return. No role-agent advice was received; recorded public-display decisions settle the scope.

## Tasks

- [x] **T1 — Build foundations and entrance boards.** Reuse available tokens/icons/brand and add readable display typography and shared trilingual rows/notices. Cover normal, arrival/origin, cancellation/delay, early-ending positions and waiting area. Check times/platforms dominate, schedule stays visible and all three scripts fit.
- [x] **T2 — Build platform boards.** Depends on T1. Cover next train, both sides of a platform move, withdrawal, cancellation, early-ending positions, cleared departure and no next train. Check no unserved train is presented as an operating departure.
- [x] **T3 — Complete evidence, offline and system states.** Depends on T1/T2. Cover ageing/unknown/partial failure, both offline surfaces, no cache, reconnect/recovery, overnight expired data, loading/empty/configuration. Check no cached ETA or effective platform appears current offline.
- [x] **T4 — Theme and validate.** Depends on T1–T3. Use Light mode on every display root and dark canvas sections, and verify editable components, fonts, text bounds/contrast and coverage. Provide static prototype starting points without passenger-facing navigation or automatic transitions.

## Boundaries

Figma-only design. Interactive station terminal, app/backend implementation, maintenance/admin UI, free-text notices and native-language validation are separate work. Fixtures illustrate contracts; origin and overnight edge cases may use clearly recorded synthetic design examples rather than claiming they are current seeded services.


## Completed design — 18 October 2026

[Open the Light entrance board in Figma](https://www.figma.com/design/38B21GKxgb3wxRar06gHXP/SmartRail-UI?node-id=231-36). Page: `HiFi · Station displays` (`229:36`).

- **33 Light-only screens**, all 1920 × 1080: 8 entrance/waiting states, 10 platform states, 4 observation states, 8 offline/recovery states and 3 system states. The initial three Dark staging boards were converted in place and all five empty Dark staging sections were removed.
- User-requested train IDs are now bold black: **48px in timetable rows and affected-service notices, 56px in platform heroes**. Enlarged platform components preserve room for the larger IDs.
- Reused HiFi semantic tokens, brand and icon instances; created shared Station header, footer, status, row, notice, physical-platform and train-hero components plus 27 display text styles. The available Sinhala foundation font is Abhaya Libre; Tamil is Noto Sans Tamil and Latin is Noto Sans.
- Digital audit checked 276 component instances and 1,319 visible text layers: no missing fonts/components, screen overflow, clipped children, wrong root theme, missing header/footer, small main train ID, or passenger interaction. Maximum content bottom is 996px, clearing the fixed footer at 1016px.
- Minimum measured text/background contrast is **4.58:1**. All 33 boards have static prototype entry points, with no automatic page transitions or on-screen passenger controls. Dark canvas surrounds the Light frames for contrast.
- Verified whole-feed offline/reconnecting examples have no cached ETA or effective-platform override presented as current. Public-copy inspection found no individual controller/driver identity or protected incident cause. UNKNOWN retains separately confirmed platforms and confirmation times.
- Visually inspected entrance, changed/unknown platform, cancellation, saved timetable, final arrival, no-cache and configuration states. Repaired the narrow Tamil planned-platform heading and replaced recovery debug wording with a plain refreshed timestamp.

Native Sinhala/Tamil review and physical four-metre/sunlight checks are **pending**. Shared-source translations are explicitly unreviewed, and supplemental wording is a design draft. The DS-F1/F2/F3 and DS-N1 origin/cleared/overnight fixtures are synthetic design examples, not claimed current seeded services. No app/backend code, build, browser test or physical display test ran for this Figma-only task.

## Passenger square icon update — 19 October 2026

Replaced the shared Station header brand mark with the supplied `passenger-icon-square` asset, using a reusable `Passenger / Square icon` component. The icon retains the 64 × 64 header slot and the station names and clock remain aligned. Checked that all 33 Light-only boards show one new icon and no old brand mark, and visually inspected the header and entrance board.
