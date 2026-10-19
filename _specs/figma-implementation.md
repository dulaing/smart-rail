# Implement the Figma designs faithfully

## Why and done

Bring the existing SmartRail apps into measurable visual agreement with the approved Figma designs. Proportions, typography, spacing, colours, icons and assets must survive implementation and later changes. A working feature or a passing build alone does not prove visual fidelity.

Done when each in-scope screen/state has an agreed reference, an implementation at its reference viewport, reviewed comparison evidence, passing accessibility/reflow checks and a retained regression baseline. Real API behaviour, permissions, offline rules and freshness meanings continue to follow the feature contracts.

## Decisions and scope

- **User confirmed, 19 October 2026:** align Figma with the accessibility requirements and agreed font/colour adjustments first, then match the reconciled designs. Do not silently preserve a visual difference as an accessibility exception.
- This request is planning only. No app, backend or Figma changes are authorised by completing this plan.
- Cover Passenger, Driver, Operations, Station and the existing RailBites staff kiosk. Inventory the actual screens before estimating effort; design-only future functionality is not automatically implementation scope.
- Existing theme boundaries remain: Passenger Light/Dark; Operations Light only; Driver light default without a new theme picker; Station and kiosk Light only. Dark staff designs do not authorise additional app themes. See [foundation](app-foundation.md), [Operations](operations-workspace.md), [Driver](driver-journeys.md), [Station](station-boards.md) and [kiosk](railbites-kiosk.md).
- **Proposed execution order:** prove the method on Driver tablet duty/briefing first, because its recent 2x Figma resize exposes scale mistakes early. Complete that app's phone adaptation, then Passenger, Operations, Station and kiosk. This order is a recommendation, not a new product priority decision.
- Use one feature file and the existing token generator/components. No automatic Figma sync, Code Connect setup, replacement app, new UI framework or separate gallery project. Follow [D15 and screen rules](../docs/06-scope-decision.md).

## What exists now

- [Shared design foundation](../shared/design/README.md): `tokens.json` and `status.json`, CSS/Dart generation, bundled Noto fonts, and recorded accessibility adjustments. `node shared/design/generate.mjs --check` detects generated-file drift and contrast failures; it does not establish screen-level fidelity.
- Passenger/Driver use Flutter and `packages/smartrail_core/`; Operations/Station use React/TypeScript. The kiosk is served by the existing Node service (`services/railbites/src/staff-page.ts`), not another React app.
- [Driver hi-fi](driver-hi-fi.md) records 2560 x 1600 tablet designs uniformly enlarged from 1280 x 800, 3x phone styling and a recent transparent-logo replacement. The token profile still describes 1280 x 800; establish the intended logical mapping before changing any sizes.
- Passenger tokens currently map 1080-wide artwork to 432 logical pixels using 0.4. Verify this against the selected current frames and target device; do not treat it as an automatic scale for every phone.
- [Operations hi-fi](ops-hi-fi.md) uses 1440 x 900 frames. [Station hi-fi](station-display-hi-fi.md) uses Light-only 1920 x 1080; the current app already selects Light. The earlier dark foundation and its statement that Station lacked hi-fi are historical, not current requirements.
- [Kiosk hi-fi](railbites-kiosk-hi-fi.md) has desktop, tablet and phone examples. Its tablet composition was uniformly enlarged and centred with extra side margins; it must not be stretched to fill the new aspect ratio.
- Foundation evidence explicitly limited the earlier Figma comparison. Existing completed implementation/design tasks are not evidence that final screenshots match. Revalidate affected visual checks, preserving prior functional evidence and unrelated edits.

These findings come from repository sources, not a fresh audit of every Figma frame. SmartRail Coordinator advice (conversation `cnv_fc2b0100d3364cf68b15`) informed the explicit reference gate below; it did not determine scope or approve designs.

## Implementation method

### 1. Establish the reference before writing UI

Use the existing [SmartRail UI file](https://www.figma.com/design/38B21GKxgb3wxRar06gHXP/SmartRail-UI). For each screen family, record in this feature's evidence a compact mapping: Figma node/link and version or dated export, app route/state, theme/language, frame dimensions, target logical viewport, device pixel ratio, safe-area treatment, assets/fonts and governing behaviour spec. Store reference images and comparison artifacts alongside the relevant app's visual checks; do not create another planning document.

Read Figma measurements, variables, component variants and Auto Layout constraints through the available Figma tools. Use screenshots to verify appearance, not to guess dimensions. Reconcile recorded font/colour adjustments, 7:1 Driver/Station text requirements, newer behaviour contracts and any missing states in Figma. Check normal, selected, disabled, focus, loading, empty, error and offline variants where applicable.

Freeze the resulting reference and the acceptance rules before the first implementation. Bring unresolved device-size, behaviour or intentional appearance decisions to the user; leave only the dependent screen family blocked. This gate approves a concrete reference, not a general permission to redesign.

### 2. Translate units and constraints explicitly

Figma canvas pixels, CSS pixels, Flutter logical pixels and device screenshot pixels are different quantities. Record the intended conversion for each surface. A 2560-pixel tablet frame does not imply 2560 logical pixels; confirm the device's logical viewport instead of inferring it from its display resolution. Normalise comparison images with one documented uniform conversion, never independent width/height stretching.

Measure content width, columns, margins, header/nav heights, card bounds, padding, gaps, baselines, icon boxes, radii and shadows. Match font files, weights, size, line height and letter spacing; preserve original logo/image aspect ratios and intended crops. Inspect Flutter Material defaults and browser defaults that add unintended padding, tint, minimum sizes or margins.

Use the measured Auto Layout relationships to implement flexible rows/columns and explicit breakpoints. At reference size, reproduce the reference. Between sizes, preserve hierarchy and readable reflow. Large text, keyboard and safe areas must not be handled by scaling the entire UI down. Establish compact/overflow behaviour where a Figma example is missing before implementing that dependent layout.

### 3. Implement and verify one screen family at a time

Start with the Driver duty/briefing slice in the existing shell and controller flow. Correct shared token/profile issues once, then build the necessary app-local components. Keep reusable presentation separate from existing reads/actions without introducing another architectural layer. Reuse verified patterns within the same surface; do not force a station board and a phone into the same density.

Use deterministic test-only fixtures for visual comparison: the same text, train IDs, dates, statuses and list lengths as the reference. Freeze clocks, freshness ages and animations; load the real fonts and assets before capture. Keep fixtures out of production data paths. After visual acceptance, exercise the same views with real APIs, permissions and failures.

### 4. Make visual acceptance repeatable

For every representative screen/state, retain the Figma export, app screenshot, 50% opacity overlay, difference image and concise discrepancy/result notes. Compare the same content area, logical viewport, theme and locale. Crop documented system chrome consistently; do not crop away overflow or problem areas.

- **Geometry:** proposed starting tolerance is at most 1 logical pixel for measured major bounds, gaps and alignment at the canonical viewport, with no cumulative drift. Calibrate this on the first slice before freezing it. Text wraps, visible row counts, content hierarchy, logo proportions and fixed-element placement must match the reconciled reference.
- **Style:** token values, font files/weights, icon assets, radius and shadow definitions must match. No unexplained fallback font, substituted icon or screenshot embedded as the UI.
- **Raster differences:** Figma, Flutter and browsers can rasterise text differently. Review those differences separately; do not demand identical text pixels across renderers or hide geometry problems behind a generous whole-image percentage. Record any permitted raster tolerance on the first slice.
- **Accessibility and adaptation:** applicable contrast/targets, keyboard focus, semantics, long Sinhala/Tamil text and 200% text checks must pass. Station must retain simultaneous languages, complete notices and the simulation footer at its target resolution. Physical-distance and native-reader checks remain separate, honestly pending until performed.
- **Change control:** the user approves intentional visual deviations/reference changes. Never refresh baselines just to make a failure disappear.

Use Figma overlays for initial fidelity, then approved app-rendered baselines for regression. Flutter's existing `flutter_test` supports [golden comparisons and real-font loading](https://api.flutter.dev/flutter/flutter_test/matchesGoldenFile.html). For the web, use focused [Playwright screenshot comparisons](https://playwright.dev/docs/test-snapshots) with the same OS/browser/fonts used to create the baseline. Keep the checks inside the existing apps; no hosted visual-testing service is needed. A regression baseline proves stability only after its Figma fidelity was reviewed.

## Tasks

- [ ] **T1 — Establish agreed visual references. First ready task.** Inventory current Figma frames against existing routes/states across all five surfaces; reconcile accessibility/fonts, scale mappings, assets and behaviour conflicts. Update the existing Figma/token sources only where required, regenerate outputs and record each surface's reference mapping here. Check: selected reference exports match the recorded viewport/scale interpretation; the user has resolved intentional deviations and device assumptions; `node shared/design/generate.mjs --check` passes. Unsettled choices block their dependent families, not unrelated discovery.
- [ ] **T2 — Prove fidelity on Driver duty/briefing, then complete Driver.** Depends on T1 for Driver and shared tokens. Work in `apps/driver_app/` and `packages/smartrail_core/` as needed. First accept one tablet slice with overlay/diff evidence and calibrated tolerances, then apply verified patterns to existing phone, updates, reports and account/recovery states. Check: canonical tablet and phone captures match reconciled references; targeted goldens, Flutter analysis/tests and real Android briefing/report smoke checks pass. Revalidate [driver-journeys](driver-journeys.md) checks affected by layout changes; preserve its offline/session policy.
- [ ] **T3 — Match Passenger screens and both themes.** Depends on T1 Passenger references and the comparison method proven in T2. Work in `apps/passenger_app/` and existing shared presentation. Cover existing Home/Trains/detail, account and implemented Food/saved-journey states; record unavailable feature prerequisites instead of building them here. Check: overlays/goldens at the agreed phone viewport, Light/Dark and long-language/large-text checks pass; existing guest navigation, route selection and account flows still work on Android.
- [ ] **T4 — Match Operations workspaces.** Depends on T1 Ops references and the comparison method proven in T2; independent of T3. Work in `apps/ops-web/src/`, retaining existing auth/router/workspace logic. Cover existing corridor/detail, compose/review/results, reports/incidents, role/access/account and demo-control states. Check: reviewed 1440 x 900 comparisons plus agreed narrow-layout checks, build/lint/tests and real role-specific read/write smoke checks pass. Preserve explicit review, conflicts and uncertain outcomes.
- [ ] **T5 — Match Station boards.** Depends on T1 Station references and the comparison method proven in T2; independent of T3/T4. Work in `apps/station-web/src/`. Cover entrance/platform and existing disrupted, ageing/unknown, offline/no-cache and configuration states. Check: reviewed 1920 x 1080 comparisons, complete trilingual content/footer, no clipping, build/lint/tests and actual offline/reconnect smoke checks pass. Reconcile Figma row-density examples with the current board's content-fitting rules before changing behaviour.
- [ ] **T6 — Match RailBites staff views.** Depends on T1 kiosk references and the comparison method proven in T2; independent of T3–T5. Update presentation in `services/railbites/src/staff-page.ts` and existing assets. Cover existing login/list/detail/code entry and failure/session states at desktop and documented compact sizes. Check: reviewed comparisons, keyboard/overflow checks, service build/tests and real Ready/code-verification smoke checks pass; preserve point scope and secret handling. Any unavailable passenger callback evidence stays recorded in [kiosk](railbites-kiosk.md).
- [ ] **T7 — Verify the complete visual handoff.** Depends on T2–T6. Run shared token checks, affected app checks and retained visual regressions; inspect representative real-device states and the agreed responsive matrix. Check: each in-scope family has reference/actual/overlay/diff evidence, no unexplained visual deviation, and accessible reflow. Record unavailable devices, native readers or physical display checks as outstanding, never passing. Revalidate affected completed visual checks in the existing feature specs without changing their completed functional history.

## Boundaries and remaining setup

No backend/API redesign, new product workflows, expanded permissions, theme expansion or automatic implementation of every prototype link. Missing feature behaviour belongs to its existing spec. Existing auth, privacy, freshness and offline contracts remain authoritative.

The first task must establish current Figma versions and target device logical sizes; this planning pass did not measure them live. Proposed order and geometry/raster tolerances need calibration against the first concrete reference. No implementation, Figma edits, screenshots or acceptance checks have been performed for this feature yet.
