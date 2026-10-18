# RailBites kiosk staff page

## Outcome and context

Provider staff see only their authorised collection point's orders, mark Ready, verify the customer's entered pickup code for Collected, or request No-show only when SmartRail proves passage. Train-driven pickup changes/cancellations remain visible and callbacks reach the passenger. Follow [handoff 10](../docs/app-spec-plan.md#10-railbites-kiosk-staff-page--copy-this-prompt), [RailBites backend](railbites.md), [provider OpenAPI](../services/railbites/openapi.yaml) and [passenger food](passenger-food.md). This is a small provider-owned page served by `services/railbites/`, not another SmartRail client or the station terminal.

## Current source

`src/server.ts`, `auth.ts`, `config.ts` and `kiosk.ts` already provide point-scoped API-key routes under `/v1/kiosk/collection-points/{id}/orders`: list and POST `/{orderId}/ready`, `/collected` (`pickupCode`) and `/no-show`. Another point's key gets 403; another point's order is 404. Service/admin keys do not substitute for kiosk keys. These are machine API credentials, not a staff browser session.

`orders.ts:kioskView` **already omits the secret pickup code**. Preserve that property in every HTML/JSON response. Its current view supplies order ID/point, state/sequence, items, pickup time/evidence and cancellation reason, but not train number/service date/stop identity; add only those useful non-secret fields already stored in `OrderRow`, update the OpenAPI kiosk schema and contract tests. No schema migration is needed just to expose stored train identity.

`markCollected` checks the entered code server-side even on an identical repeat. `markNoShow` performs the authenticated SmartRail passage read and rechecks current order state inside the transaction; schedule or kiosk input is not proof of passage. Keep those business functions and callback enqueue transactions unchanged.

## Collection-point login — user confirmed

Use **one shared staff login per collection point**, with a server session. Provision a separate human password hash and collection-point mapping through provider configuration/secrets; no individual staff accounts, identity database or new identity provider. Document local provisioning/password-hash generation without putting passwords into Git or command output. Shared sessions identify the collection point, not a named individual; do not claim individual staff attribution.

Internal `RAILBITES_KIOSK_KEYS`/service/admin keys stay server-side. The human types the separate staff password over HTTPS; no internal key is embedded in JS, URLs, localStorage or the page. Password verification uses a standard salted password hash; the login form's collection point cannot override the authenticated session's scope.

## Session boundary and page

- Serve `/staff/login`, `/staff`, logout and small same-origin session-protected actions from the existing Node server. Store only opaque random session IDs in HttpOnly, SameSite=Strict cookies; session state fixes the collection point. Rotate IDs on login, expire sessions with a bounded configurable lifetime, and clear on logout/restart. HTTPS uses Secure cookies; a documented explicit local-development HTTP mode may omit Secure, never a production fallback. Do not add another service solely for sessions.
- Every read/action resolves the point from the session and checks any route target against it. Reuse existing kiosk business functions in-process with the server's authorised point, rather than exposing or proxying a key into the browser. Protect state-changing requests with a synchroniser CSRF token and same-origin validation, including logout; login is POST, rate-limited with generic failures. Keep passwords/codes/tokens out of logs and traces. The selected login policy owns provisioning, not a new admin dashboard.
- The page displays point name, date, open/completed status filters, train/date, items/quantities, scheduled versus last-reported pickup time, evidence age and reasoned cancellations. Use a plain responsive layout and ordinary controls; a branded enterprise design system is unnecessary. Poll every 15 seconds while visible and refresh after actions, preserving per-order status sequence so late reads cannot regress a terminal result.
- Ready is a deliberate action from ACCEPTED. Collection opens a blank code input; do not prefill, display, infer or save the customer's code. Clear it on success/cancel/session loss. Wrong code 422 leaves the order uncollected. No-show asks the existing server operation; UNKNOWN/NOT_PASSED/SmartRail unavailable is a refusal/unknown state, not permission to guess from a departure time.
- Disable the submitted action while in flight; refresh after lost responses or another staff member's action. Existing idempotent repeat rules handle a repeated desired result, while terminal conflicts show latest state. An expired session hides orders, clears code entry and returns to login. Never optimistically mark Collected or promise the passenger callback arrived before SmartRail confirms it.

## Tasks

- [ ] **T1 — Establish the shared point-scoped staff session.** **First ready task.** Add minimal provider password-hash configuration/provisioning, login/session/routes and serving support in `services/railbites/src/`. Check login/logout/expiry/restart, fail-closed missing credentials, CSRF/origin refusal, rate limiting, server-side point scope, another point denied and no internal key in bundle/storage/responses. Use separate human credentials and existing provider business rules; no individual account system.
- [ ] **T2 — List the correct point's orders honestly.** Depends on T1. Extend safe kiosk DTO/OpenAPI fields and build the small page. Check real orders/items/pickup times/reasoned cancellation, empty/loading/provider failure, poll refresh and session expiry; responses never include `pickupCode`. No menu/stock editor.
- [ ] **T3 — Ready, collect by entered code and prove No-show.** Depends on T2. Check wrong/correct code, repeated click, concurrent staff actions, terminal conflict, current/unknown/unavailable passage and unchanged order when passage is not proven. Use a real provider/SmartRail for the passage check, not a browser boolean.
- [ ] **T4 — Verify callbacks reach the passenger.** Depends on T3 and passenger-food T4 for screen evidence. Walk staff Ready → customer-code Collected → passenger status; stop/restart SmartRail during callback delivery and observe durable recovery using existing backend workers. Run provider build/tests, focused browser checks, keyboard/accessibility review and a second-point session boundary test. Record API-only/mobile substitutions honestly.

Planning only; login scope is settled and no new checks have run. Provider serving/session/config/backend edits are explicitly included. No payment UI, refund, inventory/menu dashboard, large provider identity system, SmartRail-secret reuse or interactive station terminal.
