# Organize the web apps by feature and adopt TanStack Query

## Why and done

Make Operations and Station easier to navigate: a feature's screens, API calls, hooks, types and rules should live together. Use TanStack Query to manage remote requests and refreshes while preserving the working apps.

Done when both apps use the structure below, their remote reads use TanStack Query, and the existing routes, appearance, permissions, actions, freshness and offline behavior pass the regression checks. Moving files alone must not change behavior.

## Decisions and boundaries

- **User confirmed, 19 October 2026:** cover `apps/ops-web` and `apps/station-web` only, including TanStack Query adoption. Preserve current functionality.
- Follow the organization in `C:/Projects/260728-FE-Architecture-Worktrees/repo-b-feature/src`: `app/`, `features/`, `shared/`. That example uses ordinary effect-based reads; it is a folder reference, not an existing TanStack Query implementation.
- This file is an implementation plan. Creating it does not start implementation.
- The folder names and migration approach below are engineering recommendations for this plan. No backend/API changes, Flutter or kiosk reorganization, new routing library, visual redesign, new app package or shared React framework is required.
- Preserve the current shared tokens, fonts, translations, Light themes, design sample and URLs. Coordinate with [Figma implementation](figma-implementation.md); restructuring does not complete its visual-fidelity tasks.

## What exists now

- Operations mixes session, account, routing and shell files at `src/`. `workspace/` combines public API transport, polling, corridor/journey reads and change workflows. `reports/` combines the report inbox, incidents, audit history and reusable paging. Several report screens import infrastructure from `workspace/`.
- Operations `workspace/hooks.ts` uses `usePolled` and `poller.ts` for visible 15-second refreshes, cancellation and reconnect. The pure `corridorLoader.ts`, `journeyLoader.ts`, change reducers and action functions already encode important failure rules.
- Station keeps almost everything directly under `src/`. `useBoard.ts` schedules `feed.ts` refreshes through `FeedClient`; `snapshot.ts` persists only the public timetable. `vite.config.ts` and `sw-template.js` provide the offline shell.
- Neither web app currently declares `@tanstack/react-query`. Operations tests already use a recursive source glob; Station's `node --test src/*.test.ts` will miss tests moved into feature folders.
- Behavioral contracts remain [Operations workspace](operations-workspace.md), [reports/incidents/audit](operations-reports.md), [Station boards](station-boards.md) and [identity/permissions](identity-and-permissions.md). Some opening descriptions in those specs describe earlier starters; use current source and their later implementation evidence.

## Target structure

Each app owns its own `app`, `features` and `shared` folders. Repository-level `shared/design/` stays the source for generated design files.

```text
apps/ops-web/src/
  main.tsx
  app/                  # App, Shell, router, providers, queryClient, global styles
  features/
    account/            # Account screen and sign-in state components
    operations/         # Corridor, journey, preview/confirm/withdraw flow
    reports/            # Driver report inbox, detail and acknowledgement
    incidents/          # Incident list/detail/create/resolve and selection API
    audit/              # Read-only journey and incident history
  shared/
    api/                # HTTP clients, common rail DTOs and journey lookup reads
    auth/               # Keycloak session, Me type, access helpers
    ui/                 # Existing reusable status/freshness components
    lib/                # Genuinely shared time, formatting and cursor utilities

apps/station-web/src/
  main.tsx
  app/                  # App, providers, queryClient, global styles
  features/
    station-board/
      components/       # StationBoard, EntranceBoard, PlatformBoard, Clock, parts
      api/              # FeedClient, feed composition, query keys/options
      hooks/            # useBoard and local clock hook
      lib/              # Board derivation, config, messages, snapshot, time
      types/            # Board/feed DTOs where a separate file is useful
  shared/
    ui/                 # Existing reusable status/freshness components
```

Within an Operations feature, use `components/`, `api/`, `hooks/`, `lib/` and `types/` when needed, following the reference. Keep component CSS beside its feature and tests beside the code they exercise. Do not create empty folders or split a small module merely to fill this layout.

### Where existing files go

| Current files/area | Destination and responsibility |
| --- | --- |
| Operations `App.tsx`, `Shell.tsx`, `router.ts`, base CSS | `app/`; keep route composition, focus handling and global styles here |
| Operations `auth.ts`, `access.ts` and access tests | `shared/auth/`; keep the existing Keycloak flow and effective permissions |
| Operations `AccountPage.tsx`, `AuthStates.tsx` | `features/account/components/` |
| Operations `workspace/` screens, loaders, change flow/actions, facts and rights | `features/operations/`; organize UI, API, hooks and pure rules internally |
| Operations `workspace/api.ts`, common staff transport from `changesApi.ts`, `live.ts` clients | `shared/api/`; leave preview/confirmation endpoints inside `features/operations/api/` |
| Operations mixed `workspace/types.ts`, `reports/types.ts`, `reports/reportsApi.ts` | Split by ownership: common rail DTOs in `shared/api/`, report/incident/audit DTOs and endpoints in their own features |
| Operations `reports/Report*`, inbox and acknowledge modules | `features/reports/` |
| Operations `reports/IncidentWorkspace.tsx`, incident action modules | `features/incidents/` |
| Operations `reports/AuditWorkspace.tsx`, audit page/text modules | `features/audit/` |
| Operations journey-choice reads used by reports/incidents/audit; generic `paged.ts` and common time/format helpers | `shared/api/` for common reads; `shared/lib/` for genuinely reused pure rules |
| Both apps' `design/` components and CSS | App-local `shared/ui/`; continue importing repository design outputs |
| Station `App.tsx` board logic, board views, `parts.tsx`, `Clock.tsx`, feed/config/snapshot modules and fixtures | `features/station-board/`; make `app/App.tsx` a small composition entry |

Imports flow from `app` to features/shared, and from features to shared. Shared code must not import a feature. A feature may use another feature's explicit API/type entry for existing integration, such as selecting an incident or opening one from a report; avoid importing its private UI/reducers or creating a cycle. Keep relative imports initially so existing Node tests need no alias loader. Update both READMEs with the placement rules.

## TanStack Query approach

Use the stable React Query v5 API (`@tanstack/react-query`), with each app's manifest and lockfile updated together. Create one stable `QueryClient` per app session through `app/queryClient.ts` and `app/providers.tsx`. Keep query keys/options and request hooks with their owning feature; common journey lookups can live in `shared/api/`.

### Reads and refreshes

- Use `useQuery` for corridor/journey snapshots, board feeds, report/incident lists and details, and journey choices. Initially wrap the existing pure composite loaders instead of rewriting their domain logic into unrestricted parallel endpoint queries. Preserve the current concurrency limits and per-resource ETags.
- Include every identity/filter that changes a result in serializable keys: service dates, journey/station IDs, filters, audit subject and cursor. Protected Operations keys also include account/session identity and an effective-permission/scope fingerprint. Never put tokens in keys. Do not show another filter/station/account's previous data as the new view's result.
- Configure the existing live reads for 15-second visible refreshes, immediate refresh on visibility/reconnect and no background polling. Set `retry: false` to retain the current failure timing. Use explicit focus/reconnect settings; give a shared query one polling owner so multiple observers do not add independent timers.
- Pass the query `AbortSignal` through all read transports, combining it with the existing timeout rather than replacing the timeout. Abort/discard superseded answers. Coalesce refresh triggers without overlapping the same refresh or leaving the old polling loop running beside Query.
- Keep the local clock and domain freshness calculations. Query `staleTime`/`dataUpdatedAt` describe cache bookkeeping; they must never replace observation timestamps, server clock correction or CURRENT/AGEING/UNKNOWN rules. A 304 must not make an observation new. Include date rollover in the key/update logic and reject a refresh calculated for an obsolete service date.
- Preserve partial failures as domain results. Existing transports often return tagged results rather than throwing: adapt them deliberately so a denial/failed read cannot become a generic successful empty list. Display meaningful initial loading, empty success, denied, missing, stale and unavailable states. Do not let Query's retained data obscure a 401/403.
- For composite live reads, use `networkMode: 'always'` with `retry: false` and explicit reconnect refresh. This allows the existing failure-to-offline transition to execute rather than leaving the query paused while old live data stays visible. Verify real network failures as well as browser online/offline events.

### Actions, pagination and session safety

- Keep drafts, selection, reviewed versions, validation and the existing recovery reducers as local state. Use `useMutation` to expose pending state around explicit actions, including preview, confirmation, withdrawal, acknowledgement and incident create/resolve. A resolved function returning a refused/unknown tagged result is not confirmed success.
- Keep `retry: false` for mutations and use `networkMode: 'always'` so an action is attempted once or fails immediately; it must never pause and replay after reconnect. Retain synchronous in-flight click guards: `useMutation` alone does not prevent duplicate calls. No optimistic operational success, mutation persistence or queued mutation scope.
- Preserve stale-preview 409 handling and the exact reviewed request. A lost write response keeps the existing uncertain-outcome message and authoritative reconciliation read; no automatic second POST. Confirmation reconciliation still requests a full answer through the existing `fresh` option.
- After confirmed writes, update/invalidate only relevant query families: change confirmation/withdrawal affects its journey and corridor; acknowledgement affects report detail/inbox; incident actions affect incident detail/list and open-incident selectors. Refresh permitted audit reads when relevant. An incident resolving during review must still invalidate that review. Acknowledgement/incident resolution must retain their existing actor/time and independent-state rules.
- Keep report/incident first-page refresh separate from explicit cursor loads. Reuse the current paging rules for historical page assembly, selection and deduplication, while Query owns requests. Do not replace them with default infinite-query refetching that reloads every older page or changes cursor snapshots. Audit remains an explicit paged read with "Check from the start", not a new 15-second poll.
- On logout, account change, account disablement or detected permission change, cancel relevant reads, remove protected cached data and reset local drafts/review/mutation state. Ignore callbacks from the old session. Clearing a mutation cache cannot undo a POST already sent. Keep tokens in memory and protected responses out of browser persistence; preserve the existing sign-in/deep-link and refresh flows.

### Station persistence

Query's in-memory cache supplements the existing versioned `smartrail.station-board` timetable store; it does not replace the offline contract. Keep that key/version and shell service worker working across this refactor. Persist only station names, dated timetables and download metadata, never running estimates, effective platforms or active changes as current data.

On whole-feed loss, clear live presentation and show timetable-only rows and the existing banner, even if Query holds an older successful result. Preserve partial-row failures, next-date prefetch, previous-date overnight services, clock and simulation footer. Reconnect must obtain authoritative live reads before restoring live presentation. The feed contains a `Map`; use `structuralSharing: false` for that aggregate or an explicitly tested compatible representation.

Official sources checked on 19 October 2026: [defaults](https://tanstack.com/query/latest/docs/framework/react/guides/important-defaults), [polling](https://tanstack.com/query/latest/docs/framework/react/guides/polling), [query keys](https://tanstack.com/query/latest/docs/framework/react/guides/query-keys), [cancellation](https://tanstack.com/query/latest/docs/framework/react/guides/query-cancellation), [network modes](https://tanstack.com/query/latest/docs/framework/react/guides/network-mode) and [mutations](https://tanstack.com/query/latest/docs/framework/react/guides/mutations). The policies above are this project's application of those APIs.

## Tasks

- [ ] **T1 - Run Operations from feature folders.** Ready; no dependency on other tasks. Before moving files, record current route/screens and existing check results in this file. Reorganize `apps/ops-web/src/` using the mapping above, including reports/incidents/audit ownership and shared transport extraction. Preserve the current polling/actions in this step. Update imports, CSS/asset paths, fixture paths, TypeScript inclusion and README. **Check:** Operations test/lint/build plus signed-in navigation, direct journey/report/incident/audit/account URLs, permission-based navigation and the design sample work with the same appearance and behavior.
- [ ] **T2 - Run Station from feature folders.** Ready; independent of T1. Record its baseline, then move board logic and related tests under `features/station-board/`, with app composition and shared UI separated. Keep the feed/persistence unchanged. Make the Station test command recursive and verify that all moved tests are still discovered; repair shared freshness-fixture, CSS and font paths. Update README. **Check:** Station test/lint/build; entrance, platform, invalid config and design sample match the baseline; a production build still loads the board and bundled fonts offline.
- [ ] **T3 - Load Operations journeys and changes through Query.** Depends on T1. Add the dependency, provider, query client, session-boundary handling and operations query/mutation hooks. Migrate corridor/journey and composer reads/actions, retaining the loaders/reducers and existing staff/session semantics. Other feature pollers can remain until T5. **Check:** focused Query/client tests and a real staff flow cover ETag ageing, bounded/no-overlap reads, superseded journeys/dates, permission loss, one POST per confirmation, stale 409 and lost-answer reconciliation; test/lint/build pass.
- [ ] **T4 - Refresh Station through Query without changing offline behavior.** Depends on T2; independent of T3. Add its app-local dependency/provider and replace the `useBoard` scheduling loop with a feature query around the existing feed composition. Preserve timetable initialization/persistence, ETag transport, local clock and concurrency limits. **Check:** focused feed/query tests plus a production browser offline reload with/without a saved timetable, partial/whole outage, reconnect, service-date rollover and superseded station configuration; test/lint/build pass.
- [ ] **T5 - Load Operations reports, incidents and audit through Query.** Depends on T3. Migrate remaining feature reads, explicit action hooks and common journey choices. Preserve cursor assembly, selected details, incident/composer integration, failure distinctions and manual audit traversal. Remove `usePolled`/`createPoller` only after their final consumers are migrated; retain useful domain tests. **Check:** report filters and load-more, first-page refresh with historical pages retained, acknowledgement race/uncertainty, standalone incident response loss without replay, resolved incident invalidating review, and Auditor-only traversal; test/lint/build pass.
- [ ] **T6 - Verify both reorganized apps together.** Depends on T4 and T5. Check the acceptance list below, remove obsolete compatibility files/imports and document the final structure and Query ownership in the existing READMEs. Record actual evidence and any unavailable checks in this feature file. **Check:** both apps pass their commands and the connected Operations-to-Station drill retains current behavior and appearance.

T1 is the next ready task. T2 is also independent. Each task must leave its app runnable; keep file moves separate from request-behavior changes so a regression has a clear cause.

## Feature acceptance checks

1. For each app run `npm test --prefix apps/<app>`, `npm run lint --prefix apps/<app>` and `npm run build --prefix apps/<app>`. Compare discovered test coverage with the pre-move baseline; no lost tests from changed globs. Run `node shared/design/generate.mjs --check`. Add only focused tests for new Query/session/refresh behavior that existing tests do not cover.
2. Inspect the final imports: features own their related files, shared modules have no feature dependency, and old `workspace/`, `reports/` or flat board files do not remain as parallel implementations. Both development samples, generated status/freshness imports, fonts and production asset paths work.
3. With the actual local stack, check Controller, station-scoped staff and Auditor access; login/deep-link/token refresh/logout; revoked permissions and switching accounts with a request in flight. Protected cached content must disappear on denial and must not reach the next account. Offline/reconnect and repeated clicks must not replay a write.
4. Exercise preview/confirm/withdraw, two-editor stale preview, acknowledgement and incident actions. A deliberately lost write response causes reconciliation and no duplicate POST. Report/incident filters and cursors retain the old selection/history rules; audit reaches the same end-of-history.
5. Make a permitted staff platform change and verify it reaches entrance, original-platform and destination-platform boards. Repeat partial failure, complete live loss, offline reload with/without cache, reconnect and Colombo midnight/overnight cases. Verify 304s and elapsed time still reach the shared freshness thresholds; cached changes/ETAs never return as current after offline reload. Withdraw the drill's own change afterwards.
6. Compare the same before/after states at Operations 1440 x 900 and Station 1920 x 1080, including long Sinhala/Tamil text, footer visibility, keyboard focus and Operations 200% zoom. This is a preservation check against the current app; separate Figma/physical-display/native-reader acceptance remains governed by its existing specs.

No checks have been run for this plan. Existing completed feature tasks keep their historical evidence; the behavior affected by restructuring must be reverified here. If the API, identities, browser or other prerequisite is unavailable during implementation, record the exact missing check and leave the affected task incomplete.
