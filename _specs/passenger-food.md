# Passenger food

## Outcome and context

A guest can browse food in the separate Food tab, then sign in to order for an eligible future stop, retain one order through an uncertain response, and see its pickup code/time/status. Pay at collection. Follow [handoff 8](../docs/app-spec-plan.md#8-passenger-food--copy-this-prompt), [app foundation](app-foundation.md), [passenger trains](passenger-trains.md), [RailBites backend](railbites.md), existing Figma food flows and [provider contract](../services/railbites/openapi.yaml). This extends `apps/passenger_app/`; the app calls SmartRail only.

## Current contracts

`services/api/Modules/Catering/Orders/BrowseEndpoints.cs`, `OrderEndpoints.cs`, `OrderDtos.cs` and `OrderCreator.cs` already provide the following. Reuse exact serialized field names and stable problem codes, rather than designing another order API.

| SmartRail route | Meaning |
| --- | --- |
| GET `/api/journeys/{journeyId}/collection-options` | Public options: `stopId`, `stationId`, `collectionPoint`, scheduled/estimated arrival, `orderBy`, `orderable`, `reasonCode`. |
| GET `/api/food/collection-points/{collectionPointId}/menu` | Public menu; provider prices are integer minor LKR, not floating-point rupees. Server cache is at most 60 seconds. |
| POST `/api/food/orders` | `clientRequestKey`, `collectionPointId`, `journeyId`, `pickupStopId`, items `{menuItemId,quantity}`. Account derives from the token. |
| GET `/api/food/orders` and `.../{id}` | Own order links only. Current list is latest bounded orders (up to 100), not an unlimited history. |
| POST `/api/food/orders/{id}/cancellation` | Server/provider decide whether cancellation is currently allowed. |

New placement returns 201 if accepted, 202 while uncertain, 422 on refusal and 409 if a key is reused for different input. A replay can return 200 or the same pending/refused result. 202 includes the stable link/Location to poll. If the entire create response was lost before the app learned its link ID, replay the identical payload with the same account/key to discover that link; do not infer a result from an empty bounded order list.

`OrderDto` includes `id`, `status`, `statusSequence`, dated journey/pickup stop, nullable `pickupCode`, items/`totalMinorLkr`, `pickupTime` with `etaEvidenceAt`, `reasonCode`, created/updated times. Status is `PLACING`, `REJECTED` or provider `ACCEPTED`, `READY`, `COLLECTED`, `NO_SHOW`, `CANCELLED`. No PREPARING state exists in this contract.

## Flow and failures

1. Food opens without a login wall. Select a dated journey using the train context, then a collection option the server identifies as eligible. Show why unavailable options cannot be ordered, including UNKNOWN, passed/unserved stop or wrong service date. Demo pickup points are PLG/PDN on outbound SIM-3001; the opening GPH → FOT train is not automatically a food-demo route.
2. Browse items/quantities, show prices/totals as LKR from integer minor units, then review stop, collection point, pickup estimate/evidence age, pay-at-kiosk policy and No-show policy. Preserve route/menu/cart context through optional Keycloak sign-in; cancellation returns to public content. No credentials/provider keys in Flutter.
3. Before first submission persist a UUID request key and immutable canonical payload under the signed-in account. Repeated taps, request loss and restart use the same key. Once submission begins, do not edit that payload under its key; only a conclusively rejected/completed outcome permits a deliberately new submission. Do not store pickup codes or pending requests in the public timetable cache.
4. **PLACING:** say **Order outcome not yet confirmed**. Retain the link, poll every 30 seconds while visible, refresh on resume/reconnect, and offer same-key recovery without creating another order. An ambiguous earlier order stays recoverable even if train eligibility/cutoff subsequently changes. Server reconciliation handles this; the client must not declare it rejected or generate a fresh key to bypass it.
5. **Confirmed order:** show pickup code only in the authenticated own-order view; retain provider-reported pickup time and its evidence age. Poll server status and refresh on resume. Accept newer `statusSequence` only within this order's current state; a late HTTP response cannot regress Ready/Collected/Cancelled. Handle final refusal and cancellation codes with translated fixed wording, preserving unknown codes as a useful generic message rather than false success.
6. Cancellation is a server-approved action. Do not cache/hardcode the provider cutoff or promise eligibility from a client countdown. Refresh options/order state, send one cancellation and show 409/503 honestly; reconcile a lost response by rereading before retry. UNKNOWN blocks a new order but does not cancel an existing one. Cancellation/early ending beyond pickup cancels only affected open orders; withdrawal does not revive them. Collected/No-show remain history.

Preserve account ownership through 401/403/logout/switch: hide codes and personal data immediately, cancel old requests and remove its local private content. After a fresh sign-in, reload own orders and reconcile any known link; never submit the previous account's pending payload as a new user. No offline ordering/cancellation. An unavailable food provider leaves public train browsing usable. Empty menu, stock/cutoff changes, partial browse failure and slow/pending placement are distinct states. Use both passenger themes and foundation accessibility/localisation plumbing; this spec does not claim food text has native-reader approval.

## Tasks

- [x] **T1 — Browse real collection options and menu.** Depends on app-foundation T1/T2 and passenger-trains T2 for dated context. Add Food navigation/screens/read state under `apps/passenger_app/`. Check guest browsing, correct stop-occurrence selection, LKR minor-unit formatting, empty/provider-error states, unknown/unserved/passed pickup and route changes. **First UI task.** Done in `apps/passenger_app/lib/food/`: prerequisites (app-foundation T1/T2, passenger-trains T2/T3) exist. Checked with Flutter controller/API/widget tests (stale-response races, Colombo today, unknown/unserved/passed/wrong-date reasons from the server, integer LKR, empty menu, provider outage, both themes, 200% text in three languages); not yet run against the real stack, which T5 owns.
- [ ] **T2 — Review, sign in and submit one durable request.** Depends on T1. Add cart/review and per-account request-key persistence using existing auth. Check login cancellation retains public access, repeated taps use one key, no edits reuse a key, and the actual request excludes client-supplied ETA/cutoff/actor fields. Real backend accepts/refuses current stock/eligibility.
- [ ] **T3 — Recover pending placement after timeout/restart.** Depends on T2. Check loss before/after link-ID response, stable-key replay, 202 polling and app restart against real SmartRail/RailBites; one provider order survives. Change eligibility during the uncertainty and verify no duplicate, false final rejection or new-key workaround. Include 409 key-conflict and account-switch isolation.
- [ ] **T4 — Track pickup and terminal status, then cancel safely.** Depends on T3. Check delay updates pickup time/evidence, provider cutoff changes 30→45 without an app release, callback-driven Ready/Collected/No-show, server-approved cancellation and affected-order cancellation after early ending. Old responses do not regress status and withdrawal does not revive an order.
- [ ] **T5 — Prove the device food journey.** Depends on T1–T4 and railbites-kiosk for staff UI evidence. Use actual Android login, order, pending recovery, pickup code and callback status; combine focused widget tests with the real-stack flow. Run Flutter analysis/tests, both themes, large text/screen-reader checks and privacy/cache checks. API-only substitutes and unavailable provider/drill evidence remain labelled explicitly.

Planning only; no new checks have run. RailBites T6 is currently checked in its file, but its trailing evidence paragraph still says the local drill/AppHost/hosted CI were not verified. Treat that inconsistency as a verification prerequisite owned by RailBites T6, not proof this client or local rehearsal has passed. No edits, online payments/refunds, ratings, provider-direct requests or background push.
