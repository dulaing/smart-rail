# RailBites kiosk hi-fi

## Outcome and decisions

Create editable Light-only RailBites staff kiosk screens in the existing SmartRail-UI Figma file, following the 1440 × 900 wireframes and existing HiFi tokens. Use dark outer canvas sections for contrast. Include compact tablet and phone examples of the same workflow.

Follow [the current kiosk contract](railbites-kiosk.md), [provider order rules](railbites.md) and the RailBites wireframe page (`70:2741`). Current contracts supersede older wireframe behavior: one shared collection-point login; non-secret order references; blank customer-code entry with server verification; authenticated passage proof for No-show; no claim that the passenger callback has arrived without confirmation. The user requested Light only on 18 October 2026.

Service simulation controls already belong to the SimOperator Ops designs. Do not reproduce the older kiosk wireframe's global outage switch or individual staff identities. No payment, refund, stock/menu editor, provider administration or backend implementation is included.

## Tasks

- [x] **T1 — Establish the shared kiosk visual patterns and normal flow.** Reuse semantic tokens, brand, icons, controls and food photos where useful; create missing kiosk components. Cover login, scoped orders, Accepted → Ready → code entry → Collected. Check visible next actions, readable train identity and no pickup-code disclosure.
- [x] **T2 — Complete exceptions and responsive views.** Cover changed pickup/unknown ETA/cancellation, terminal history, No-show checking/refusals, loading/empty/outage/recovery, session loss, concurrent actions and uncertain responses. Include tablet and phone adaptations. Check truthful pending/terminal states, preserved station scope and no unconfirmed successful action.
- [x] **T3 — Wire and validate the Light-only designs.** Link representative flows and return navigation. Check frame/content bounds, fonts, component instances, semantic contrast, image editability and explicit Light root modes. Inspect representative compositions and repair defects.

Figma-only checks do not prove a real login, code verification, passage read, callback, responsive browser implementation or accessibility runtime behavior.

## Completed design — 18 October 2026

[Open the kiosk orders in Figma](https://www.figma.com/design/38B21GKxgb3wxRar06gHXP/SmartRail-UI?node-id=253-155). Page: `HiFi · RailBites kiosk` (`251:36`). Start-here notes: `260:1045`.

- **36 Light-only screens:** 32 desktop at 1440 × 900, two tablet at 2560 × 1600 and two phone at 390 × 1200. Six dark outer sections group access, orders/preparation, collection, No-show, train changes/recovery and responsive examples.
- Six access/session states; seven orders/preparation/history states; seven collection/error/conflict states; six No-show/check/refusal states; six train-change/outage/recovery states; four responsive examples. The full exception inventory is desktop; compact examples demonstrate orders and code entry.
- Reused HiFi Color tokens, Noto Sans, brand/icons, Ops controls/notices and existing rice/tea photo assets. Added reusable RailBites header, five-state order card and dated pickup summary components with text properties. Fixed shared card/summary child sizing so narrow instances reflow properly.
- Structural audit: 1,698 screen descendants, including 883 editable text layers, 356 component instances, 386 layout frames and 73 vectors. Six small food-photo instances are discrete content images; there are no flattened screen images. All 36 roots explicitly resolve Light mode. No missing component/font, unexpected font, unbound text fill, screen overflow or clipped child was found after correction.
- Minimum measured text/background contrast **4.575:1**. Inspected desktop order list, Ready detail, wrong-code state, login, offline list, phone orders and tablet code entry. The only visible numeric pickup-code example is the deliberately wrong customer-entered value in its input field; lists and detail summaries contain only non-secret order references.
- **160 prototype links**, including four timed fixture-outcome demonstrations; all 36 screens have named starting points and all linked destinations exist on this page. Responsive links stay within their device examples. Secondary fixture orders are not linked to the primary order's detail. Header copy distinguishes unauthenticated collection-point access from a shared authenticated session.

No app/backend files were changed or runtime checks claimed. Existing kiosk implementation tasks remain unchecked in the separate kiosk spec. The prototype illustrates provider confirmation and passage outcomes; actual input verification and callbacks require implementation.

## Tablet size correction — 18 October 2026

- [x] **T4 — Resize tablet frames without stretching the design.** User requested 2560 × 1600 with original proportions strictly maintained. The two original 1024 × 900 compositions use a uniform 16/9 scale, centered inside 2560 × 1600 frames with 369.78px additional margin on each side. Text, icons, controls and spacing share the same scale; the aspect-ratio difference is absorbed by the margins. Desktop and phone dimensions remain unchanged.

Verified both tablet frame dimensions after persistence, Light mode, 47 text layers and 17 connected component instances with no screen overflow or missing fonts/components. Restored source text sizing inside scaled instances after visual review found wrapped button labels. Final orders/code-entry screenshots pass. Canvas spacing and tablet size labels were updated; screen IDs and prototype destinations are retained.

