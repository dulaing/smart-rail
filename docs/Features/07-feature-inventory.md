# SmartRail feature inventory

Every feature mentioned anywhere in this project, whether built, thin, design-only, deferred or rejected. This is a raw inventory, not a plan: it is grouped by area so duplicates are easy to spot, and the order within each group means nothing. Where sources disagree on a value, the variants are listed together on one line.

**Source tags**

| Tag | Source |
|---|---|
| `B` | `final-assessment-scoped 2.md` (the brief) |
| `N` | `docs/00-design-notebook.md` |
| `01`–`05` | `docs/01`–`05` feature docs |
| `PS` | `docs/pilot-services.md` |
| `V1` | `C:\Projects\smartrail-system-design.html` |
| `V2` | `C:\Projects\smartrail-system-design-v2.html` |
| `S` | `docs/06-scope-decision.md` |
| `C` | `docs/ai-usage/2026-10-01-scoping-council.md` and the council reports |
| `K` | Kasun screen feedback supplied by the project owner on 8 October 2026 |
| `G` | 8 October simulated stakeholder group discussion; suggestions and claimed progress are not implementation evidence |

---

## Passenger mobile app

### Finding trains and journeys

- Answer "which train should I take?" `B`
- Journey search between two stations `B` `01` `S`
- Compare the next few trains between two stations `B` `01` `S`
- Show available connections from another station `B`
- Multi-train journey planner with changes `01` `V1` `V2`
  - V2 limits it to direct trains plus one change.
  - V1 uses a RAPTOR planner with a Rust core.
- Plan journeys that cross midnight without splitting them `V1`
- Re-plan a journey live during a disruption, and drop cached plans when the network changes `V1`
- Accessibility-constrained planning: step-free, wheelchair, hearing loop, assistance needed `V1`
- "Collect food at station X" as a journey-planning constraint `V1`
- Accessibility badge on each route `V1`
- "Why this route" explanation on each result `V1`
- Natural-language journey search ("passenger copilot") `V1`

### Status of my train

- Save a journey or train to a watch list `01` `V2` `S`
- Home screen that answers "Is my train coming?" straight away (saved-train status card) `01` `S`
- Guest opening screen: Gampaha → Colombo Fort, with a clear way to change both stations `K` `01` `S`
- First screen shows the next two or three trains: scheduled and estimated arrival at the boarding station, delay, platform and update age `K` `01` `S`
- Highlight the earliest expected arrival only when its estimate is current, it serves both stations and it is not cancelled `K` `01` `S`
- Home screen laid out as a departure board, not a map `V1`
- Train status: on time / early / late / cancelled / terminated early / unknown `B` `V2` `S`
- On-time band: up to 1 min late counts as on time; 2+ min shows as late `V2`
- Scheduled and estimated times always shown separately `01` `S` `V1`
- Actual times recorded per stop `V1`
- Estimated arrival or departure at my station `B`
- Show whether the train is running early, on time or late `B`
- Last station passed / "last seen" `B` `01`
- Next station the train will reach `B`
- Train detail as a stop list: passed, next, later `B` `S`
- Live train position on a map, shown only when recent and labelled as simulated `B` `01` `V2`
- ETA shown with an uncertainty band `V1`
- Tracking confidence shown `V1`

### Freshness

- Data-age labels such as "Live" or "Last updated 3 min ago" `V2` `S`
- Thresholds: ageing at 3 min and "unknown" at 4 `01` `S`; position "unknown" after 3 min `V2`; stale at 90 s and alert at 10 min `V1`
- "Current arrival unknown — last seen X minutes ago at [station]" `01`

### Disruptions and changes

- Disruption information: delays, cancellations, affected stations `B` `01`
- Disruption banner `S`
- Disruption cards that answer what happened, how it affects you, and what to do next `V1`
- Next option or alternative train after a cancellation `01` `V2`
- Station-closure advice: nearest open station `V2`
- Early termination advice: where the train ends now and how to continue `V2`
- Early ending keeps valid earlier stops and removes later arrival estimates and food collection options `G` `S`
- Platform-change alert `B` `01`
- Prioritise a change to the platform where the passenger boards; show later-station changes in detail `K` `01` `S`
- Departure-time-change alert `B`
- Show who confirmed a change and when (source and time) `01` `S`
- Show the delay reason `V2` `S`
- "SmartRail simulation" label on simulated data `01` `PS`

### Alerts and notifications

- In-app alert feed or inbox `S` `V2`
- Push notifications: FCM `V2`, or Azure Notification Hubs with FCM/APNs `V1`
- WhatsApp and SMS fallback chain for notifications `V1`
- Notification preferences, opt-in alerts and quiet hours, with safety alerts overriding quiet hours `V1` `V2`
- Follow a station for alerts `V2`
- One alert per change, with no spam when the ETA shifts by a minute; dedupe and per-user rate limit `V2` `V1`
- Local notifications triggered while the app is open `C`

### Offline and connectivity

- Offline timetable (downloaded schedule) `N` `01` `V2`
- Offline saved journeys, plus the last received status with an age label `N` `01` `S`
- Offline timetable packs from Blob storage, updated only when a version file changes `V2`
- SQLite cache (drift) `V2` `V1`, or a JSON-file cache `S`
- Refresh on Wi-Fi or when the version file changes `V2`
- Bandwidth-lite or low-data view `01` `V1`
- Refresh when the app resumes, and pull-to-refresh `S`
- Polling every 30 s with ETag `S`
- Food ordering disabled while offline, with the app saying so `V2` `S`

### Language and accessibility

- Sinhala, Tamil and English `01` `S`
- Large type, high-contrast and audio-only accessibility settings `V1`
- Screen readers announce status changes `V1`
- Primary actions within one-hand reach `V1`
- 24px minimum touch targets (WCAG 2.5.8) `V1`; 48dp `C`
- Light theme plus system dark mode `C`
- Severity never shown by colour alone: icon, text and colour together `V1` `S`

### Accounts and personal data

- Opens public train information without an account; optional sign-in offers "Continue without an account" back to trains `B` `V2` `K` `01` `S`
- Sign in only when saving a commute, following for personal alerts or ordering food; food stays in its own tab `K` `01` `S`
- Sign in with email and password `V2` `S`
- Sign in with Google or Apple `V2`
- Passenger self-registration `V2`
- Entra External ID for passengers `V1`
- Download my data `V2` `V1`
- Delete my account (everything removed within 30 days) `V2` `V1`
- The app never sends the phone's location `V2`

### Food ordering

- Find food options associated with my journey `B` `01`
- Choose an eligible collection station on my journey `B` `S`
- Receive food on board (the brief says "receive or collect") `B`
- Browse the menu `B` `S`
- Place an order `B` `S`
- Pickup or delivery code: 6-digit `C`, 4-digit plus QR `V2`, short code by push with SMS fallback `V1`
- Order status: requested, accepted, rejected, preparing, ready, collected, no-show, cancelled `V2` `S` `C`
- Cancel an order before the cutoff `S` `V2`
- The order follows the train, showing the new collection time after a delay `S`
- Order card shows the new collection time `S`
- Clear error for sold-out items, giving the provider's reason `S`
- Ordering blocked while the train status is "unknown" `S`
- Payment on the caterer's hosted page (Stripe, test mode) `V2`
- Pay at the kiosk on collection `S`
- Automatic refund when the train is cancelled `V2` `V1`
- Automatic refund when arrival slips past a late cut-off `V1`

### Other passenger features

- Station facilities information `B` `V1`
- Assisted-travel booking, with a short reference code that can be read aloud `V1` `V2`
- Lift and escalator status `V1` `V2`
- Delay-compensation (Delay Repay) claims `V1` `V2`
- Crowding reports `01`
- Season tickets `01`
- Seat reservations and ticketing `01` `V1` `V2`
- Coach stopping positions `01`
- Passenger feedback and photo submissions, with moderation `V1`
- AI chatbot `N`
- Grounded AI disruption explainer with an abstention path `N`, tracker S17
- Cold start under 1.5 s; journey search p95 under 300 ms; usable on 3G `V1`
- Android 11 / iOS 13 minimum device support `V1`
- iOS builds `V1` `V2`
- Distribution through app stores and Firebase App Distribution `V2`

---

## Driver / operator app

### Briefing and journey

- Assigned journeys or duty / job sheet `B` `02` `V2` `V1` `S`
- Route information `B`
- Scheduled station stops with times `B` `V2` `S`
- Timing information `B`
- Journey status `B`
- Operational notifications from control `B`
- Pre-departure briefing `02` `S`
- Assigned train or engine `02`
- Temporary speed restrictions, closures and hazards ahead (simulated) `02`
- Who issued each briefing item, and when `02` `S`
- Confirmed service changes shown on the briefing `S`
- Change list on the briefing when a disruption is published `S` `V2`
- Download the whole day's duty at sign-on `V2` `V1`

### Reports and messages

- Short timestamped reports to control `02`
- Messages with control (two-way chat) `02` `V2`
- Control acknowledges or replies, and the driver sees it `02` `S`
- Distinct report states: Not sent; Sent — awaiting Control acknowledgement; Acknowledged by a named controller at a recorded time after an explicit action `G` `S`
- Report types: departed, arrived, running late with reason, incident, cannot continue `V2`
- Observed arrival and departure times, treated as claims until a controller verifies them `V1`
- Dwell-extension reason, "awaiting path" codes, berth confirmations `V1`
- Delay-reason codes: signal failure, engine defect, restriction, flooding, other `02` `S`
- Field report as reason chips, in 3 taps or fewer, with no free text required `S`
- Urgent reason codes (obstruction, injury, fire, cannot continue) can never be queued and show "Use radio" when offline `S`
- "Urgent safety issue? Use radio" instruction on the report screen and in the offline state `S`
- Engine condition: known defects, fuel, last service `02`
- Report a new defect, with a photo when safe `02`
- Signed submissions; biometric re-authentication before submitting `V1`

### Offline

- Offline briefing showing its last update time `02` `V2` `S`
- Offline outbox for reports, with a unique ID and the event time; the server uses the event time `V2` `S`
- Queue only non-urgent reports `02` `S`
- "Not sent — control has not been told" wording `S`
- Ops sees capture time vs received time and flags late deliveries `S` `C`
- Login token lasts the whole shift, so the app works offline `V2`

### Usability and safety

- Large, high-contrast, sunlight-readable text `02` `V1`
- Glove-sized targets: 56dp `C`
- Light theme by default `S`; dark mode by default `V2`
- Designed for use while stopped; nothing to read while moving `02` `V2`
- Input locked while the train moves `S` `V1`
- Driver cannot change the timetable or platforms `V2` `V1`
- Driver cannot change cause codes that blame another party `V1`

### Devices and identity

- Tablet form factor `V2`
- For guards and on-board conductors as well as drivers `V2` `V1`
- Sign in with staff ID, once per shift `V2`
- Managed-device enrolment (Intune MDM), hardware-backed keys `V1`
- Remote device revoke `V2`
- Encrypted local database `V2`
- Telemetry uploader to Azure IoT Hub `V1`
- React Native implementation `V1`; Flutter implementation `V2` `S`

### Never built (needs railway approval)

- Crossing instructions `02`
- Single-line movement orders `02`
- Movement authority or speed orders `02` `S`
- Driver surveillance features `02`

---

## Railway operations web app

### Seeing the network

- Show what is currently happening across the network `B`
- Corridor view: each train, last reported location, update time, delay, status `03` `S`
- "Unknown" shown when train data is stale `03` `S`
- Live network map (MapLibre over Azure Maps) `V2` `V1`
- Station-strip SVG diagram `C`
- Dense network board coloured by delay band `V1`
- Red banner when the live feed drops `V2`

### Service changes

- Enter and confirm delays, cancellations and platform changes `03` `S`
- Service-change composer: train, change type, reason code, impact preview, publish `S`
- Impact preview before publishing: affected journeys, stops, displays, saved journeys and food orders `03` `S`
- Change types:
  - delay
  - cancellation
  - platform change
  - temporary station closure
  - route change or diversion
  - early termination
  - skipped stop
  - general incident notice

  `B` `V2` `S`
- Change states: Draft → Published → Withdrawn `C`
- Protection against two controllers editing at once (row version, 409) `S` `V1`
- A second controller cannot silently overwrite a newer change; demonstrate the conflict before claiming it works `G` `S`
- Publish once, deliver to every app, board, driver and the caterer `03` `V2`

### Incidents

- Incident handling: report, provisional severity, owner, actions, close `03`
- Incident states Open → Resolved `S`, or Open → Assigned → Resolved → Closed `C`
- Full incident lifecycle: reported, assessed, holding message, mitigation planned, milestones, recovery, closed `V1`
- Review the public message about every 20 minutes `V1`
- Controlled cause-code list `V1`
- Severity `03` `V1`
- Blast-radius calculation across track corridors and crew/train turnround `V1`
- Affected-services set used by every downstream consumer `V1`
- Delay-attribution records: cause code, recording point, attributed party, attester `V1`
- Incident (internal) kept separate from service change (public) `S`

### Field reports

- Field-report inbox for driver messages and defects `03` `S`
- Route field reports to operations or maintenance staff `03`
- Reply to driver messages `03`
- Turn a driver report into an incident and a service change `S`
- Verify driver-reported times before they become the official record `V1`

### Records, roles and audit

- Flag and resolve conflicts between paper and app records `03`
- Audit trail of who changed what and when `03` `V2` `V1` `S`
- Audit view for the Auditor role `S`
- Append-only audit log, with immutable WORM export `V1`
- User and role management `V2`
- Temporary access with an end date and a recorded authoriser `03`
- `validUntil` on role grants, checked on every request `S`
- Station and platform management, scoped to one's own station `V2`
- Station manager controls their station's screens and facilities `V2`
- Register display and terminal devices `V2`
- Duty assignments `V2`
- Crew and engine planning `03` `V1`

### Timetables

- Import timetables (GTFS) `V2` `V1`
- Edit timetables (timetable planner role) `V2`
- Load the timetable from a curated seed file that records its source `S`
- Express timetable changes within 7 days as GTFS-Realtime modifications `V1`

### Passenger information

- Customer information: edit passenger wording, send general notices `B` `V2`
- Urgent station-staff notices on affected screens `05`
- Switch catering off for a station `V2`

### Reporting

- Reporting and analytics `B` `03` `V1`
- Punctuality reporting against the 3-minute threshold `V1`

### System and simulation

- System health panel: outbox backlog, feed staleness, display last-seen `S`
- Simulation console: play, pause, speed, reset, inject `S`

### AI assistance

- AI drafts passenger messages, with human approval `V2` `V1` `C`
- AI suggests cause codes, showing its evidence `V1`
- AI summarises the board and triages services at risk `V1`
- AI suggests which trains and stations are affected `V2`

### Usability

- Keyboard-first use, visible focus, simplified view for station staff `V1`
- Hide actions the user can't take, with the server enforcing permissions anyway `C`

### Other later items

- Maintenance routing `03`
- Train-control charts `03`
- Crossing registers `03`
- Freight `03` `V1`
- Network-wide tracking `03`

---

## Station information displays

### Profiles

- Platform display: this platform's next train `05` `S` `V2` `V1`
  - V1 variant also shows the two platforms either side.
- Entrance board: overview plus major disruptions `05` `S` `V2`
- Waiting-area profile: overview plus an explanation of delays `05`
- Main or plaza board: next 15–20 `V2` / next 12 `V1` departures
- Arrivals or gate board, for people meeting someone `V2` `V1`
- Concourse wayfinding, including lift and escalator status `V1`
- Platform repeater: 3 rows in huge type `V1`
- Coach information on the platform screen `V2`
- One app, several configured profiles `V1` `S`

### What the screens show

- Upcoming departures and arrivals `B`
- Changes stand out through words and contrast, not colour alone; nothing flashes `05` `S`
- Sinhala, Tamil and English shown together (stacked), with no language carousel `05` `S`
- Source and time of the last update `05` `S`
- Delay-reason line on affected rows `S`
- A clock on every display `S`
- One fixed simulation footer `S`
- A cancelled row stays for 10 minutes after its scheduled time `C`
- No carousels, tickers or auto-advance; or a minimum 5 s dwell `S` `V1`
- Minimum character height and contrast rules for reading at a distance `V1` `C`

### Offline and updates

- "Live updates unavailable" banner plus the timetable when the network fails `05` `S`
- "Live updates paused" after 2 min, then scheduled times after 10 min `V2`
- Board dims to "live information delayed" after 90 s `V1`
- Recover automatically when the connection returns `05` `S`
- Update by polling every 15 s with ETag `S`
- SignalR push with 30 s check-in `V2`
- Signed, content-addressed snapshot bundles fetched from a CDN `V1`
- Service worker plus IndexedDB/localStorage cache `V2` `S`
- At least 4 hours of correct information with no connectivity `V1`
- Station edge box serving local screens `V2`

### Device management

- Station staff corrections, recorded and visible to ops `05`
- Urgent staff notices `05`
- Display chooses its own platform ("asks which platform it is at") `V1`
- Remote configuration of station, profile and locale `V1`
- Display heartbeat and last-seen monitoring `S` `V2`
- Fault monitoring; staff can report a fault and restart a screen `05`
- NTP-hardened clock `V1`
- Remote disable, no local admin, tamper-evident `V1`
- Per-device key, read-only, revocable `V2`
- Anonymous read-only access with rate limits `C`
- Audio announcements and induction loops `V1` (excluded)
- Backup power, cleaning and maintenance plan `05`

---

## Station terminal (interactive kiosk)

### What passengers can do

- Search for destinations `B`
- Explore routes `B`
- Find upcoming trains, find a train by destination or departure time `B` `04` `V2`
- View station information and facilities `B` `V1`
- View disruptions `B` `V2`
- Explore available passenger services, including food options `B` `V2`
- Plan a journey `V1`
- Book assisted travel `V1`
- Show a QR code to continue on a phone `V2`

### Presentation and access

- "My train now": expected time, platform, status, time each update was received `04`
- Cancellations, long delays and platform changes put first `04`
- Sinhala, Tamil and English selectable from the first screen `04`
- Large high-contrast text; audio output with spoken guidance `04`
- 1.5× minimum type size; screen-reader focus order `V1`
- Wheelchair-reachable placement `04` `V2`
- Station notices `04`

### Kiosk behaviour

- Offline mode: saved timetable, "Live updates unavailable" with the last update time, automatic recovery `04` `V2`
- Return to the home screen after 60 s idle; 3-minute hard timeout; clear data; no deep links out `V2` `V1`
- Kiosk lock-down (Chrome kiosk, Windows IoT) `V2` `V1`
- Staff can report a fault and restart the terminal `04`
- Backup power `04`

### Later

- Seat booking `04`
- Catering browsing `04`

---

## Onboard train systems

- Onboard Wi-Fi web page: where we are, next stop, arrival times, connections `V2`
- Onboard edge service working out the next stop and ETA locally `V2`
- Onboard router GPS position every 10 s `V2`
- Bulk sync over depot Wi-Fi `V2`
- Onboard telemetry checked against track detectors `V1`

---

## Live train information (backend)

### Data sources

- Stations, routes, platforms, scheduled stops and times from the published Sri Lanka Railways schedule `N` `PS`
- Fictional demo trains, clearly labelled `PS` `S`
- Train simulator `N` `S` `V2`
  - V2: 20 trains with random delays and dead zones.
  - S: posts every 30 s, gives the same result every run, and can be reset.
- Driver reports as observations `V2`
- Simulated signalling feed (track-section occupancy events) `V2` `V1`
- GPS position `V2`
- Combine positions from several sources and score how far to trust the result `V1`
- "Awaiting confirmation" state when a track detector contradicts the train data `V1`

### Status and ETA

- Last station passed and next station `B` `V2`
- Simple ETA: scheduled time + current delay `S` `V2`
  - V2 also subtracts recovery time built into the schedule.
- ETA as a range with uncertainty `V1`
- Never departs before its scheduled time `S`
- Never infer a cancellation from silence; never show "on time" without data `V1` `V2`
- Late-arriving pings fill in history but don't move the train backwards `V2`
- Freshness states: FRESH / STALE / UNAVAILABLE / UNKNOWN `V1`
- Four kinds of truth (schedule, observation, confirmed change, prediction) with a defined precedence `S`
- Scheduled, estimated and actual times never collapsed into one field `V1`
- Platform carries where it came from: schedule, live assignment, manual, unknown `V1`
- Use the timetable actually in force, including amended engineering-works timetables `V1`
- Predict knock-on delays from crew rosters and train turnround `V1`

### Domain model

- Service pattern vs dated journey distinction `V1` `S`
- Service calendars with exceptions (e.g. Poya days) `C` `V1`

### Publishing updates

- Board projection service: pre-rendered boards per station and profile `V1`
- Publish GTFS and GTFS-Realtime feeds `V1`
- Combine updates for the same train within 1 s `V1`
- Realtime delivery options:
  - polling with ETag `S`
  - an SSE "changed" hint carrying no data `S`
  - SignalR with groups `V2` `V1`
- Passenger communications service: templates and wording per surface `V1`
- Trilingual message templates driven by reason codes `S`
- Seed demo journeys relative to the presentation start (`seed-demo --anchor`) `S`

---

## Notifications (cross-cutting)

- Notify users when important events occur `B`
- Match disruptions to saved journeys and followed stations `V2`
- Notification worker `V2`
- Channels:
  - passenger in-app feed
  - caterer webhook
  - driver briefing change list
  - display updates

  `S`
- Push, SMS, email, WhatsApp `V1` `V2`
- Integration with station PA announcements `S`

---

## Catering

### External caterer service

- A separate external system with its own database `B` `V1` `V2` `S`
- Owns the menu, prices, stock and availability `B` `V2` `V1` `S`
- Owns payment and refunds `V2` `V1` `S`
- Kitchen state and queue `V1`
- Publishes its collection points `S` `V2`
- Cutoff policy owned by the caterer, configurable per station and per vendor `S` `V1`
- Orders API that is safe to retry `S`
- Signed order-status webhooks `S` `V2` `V1`
- Receives journey changes and ETAs (`journey.cancelled`, ETA updated, platform changed, terminated) `B` `S` `V1`
- Staff page: orders by station; mark Ready, Collected or No-show; enter the pickup code `S`
- Fault modes for testing: 503, slow responses, duplicate webhooks, conflicting availability `S` `V2` `V1`
- Push availability changes `S` `B`
- Ratings `S`
- Payment capture through a Stripe hosted checkout `V2` `V1`
- OAuth2 client credentials `V1`, or an API key `S`

### Business rules

- Order cutoff, by source:
  - ETA − 30 min `S` (provider default)
  - ETA − 45 min `C`
  - 60 min before arrival `V2`
  - 2 h before scheduled arrival `V1`
- Edits = cancel and re-order `S` `V1`
- Cancellation is free until the cutoff `S` `V2` `V1`
- The order follows the train: on a delay, the new ETA is sent and the ordering window extends `S` `V2`
- Train cancelled or terminated before the station → order cancelled automatically, passenger notified `S` `V2`
- Early ending cancels **existing** orders beyond the new endpoint, with a reason; payment at collection means no pilot refund `G` `S`
- Train cancelled → full refund, claimable within 72 h `V1`
- Missed collection → No-show after departure `S`
  - V2 variant: held 15 min, then no refund unless the train was at fault.
  - V1 variant: grace notice, then forfeited.
- Same-day journeys only `C`
- Collection confirmed by pickup code `S` `V2`

### SmartRail side of the integration

- A catering module that keeps the caterer's model out of SmartRail's own `V2` `V1` `S`
- The passenger app calls SmartRail, which calls the caterer `S`
- Eligibility worked out from the caterer's policy plus the live ETA `S`
- Menu cache: 60 s `S` / 5 min `V2`
- Idempotency key on order requests `S` `V1`
- Outbox with retries and a dead-letter queue that ops can see `S` `V2` `V1`
- HMAC signing with a replay window, duplicate event IDs dropped, sequence numbers `S`
- Circuit breaker `V1`
- Data shared with the caterer: no name, phone or email `S`; first name `V2`; pseudonymous journey reference `V1`
- If the caterer is down, train information is unaffected `S` `V1`

---

## Identity and access

### Access by user type

- Public railway information readable anonymously `B` `V2` `S`
- Passenger accounts `B` `V2` `S`
- Authenticated operational users for the driver app `B` `S`
- Organisational access for ops `B` `S`
- Different permissions for different roles; no single "Administrator" `B`

### Implemented roles

- Passenger
- Driver, limited to their own journeys
- Controller
- StationStaff, limited to their own station
- Auditor
- SysAdmin, who cannot change train status
- SimOperator
- Caterer, as a machine account

`S`

### Roles documented in other sources

- Customer Information Officer `S` `V2`
- Timetable Curator / Planner `S` `V2`
- Maintenance `S` `03`
- Manager / Reporting / Analyst `S` `03` `V2`
- Guest `V2`
- Station manager `V2` `V1`
- Network controller `V2` `V1`
- Incident manager `V1`
- Assisted traveller `V1`
- On-board conductor `V1`
- Station customer service `V1`
- Service planner `V1`
- Catering operator `V1`
- Catering fulfilment `V1`
- Engineering fault reporter `V1`
- Safety and compliance auditor `V1`
- Platform administrator `V1`
- L0–L6 ops roles (unverified SLR claim) `03`

V1 lists 16 roles in total, each scoped by role, region and operator.

### Mechanisms

- Roles mapped to permissions through a table `S`
- Deny by default `S`
- Keycloak sign-in for passengers and staff; OIDC Authorization Code with PKCE, API JWT validation, and SmartRail-owned scoped permissions **pilot decision 14 Oct**
- Self-issued JWT behind `ITokenService`, with 15-min access tokens and rotated, hashed refresh tokens `S` — **superseded by Keycloak decision**
- ASP.NET Core Identity store `V2` `C`
- Entra ID for staff, Entra External ID for passengers, MFA, conditional access `V1`
- OPA policy engine `V1`
- Row-level security in the database `V1`
- API Management token validation and quotas `V1`
- Per-device keys for displays, terminals and onboard units `V2`
- Temporary access with an expiry `03` `S`

---

## Security

- Security built in throughout the lifecycle `B`
- STRIDE threat model with trust boundaries `S`
- Authorization matrix test covering every endpoint `S`
- Tests that one passenger cannot read another's data (IDOR) `S`
- 403 tests for the station scope and the SysAdmin role `S`
- Validation with ProblemDetails errors; no stack traces outside development `S`
- Rate limiting on login and public reads `S` `V2`
- Signed, timestamped webhooks with duplicate checks `S` `V2` `V1`
- Audit log written in the same transaction `S` `V1`
- Secrets:
  - user-secrets, GitHub secrets and Container Apps secrets `S`
  - Key Vault with managed identity `V2` `V1`
- Secret scanning (gitleaks) `S` `V2`
- Dependabot and vulnerability audits for dotnet, npm and pub packages `S` `V2`
- Container image scanning (Trivy) `V2` `V1`
- SAST (Semgrep) `V1`
- DAST (OWASP ZAP baseline and API scans) `S` `V2` `V1`
- CORS allowlist, CSP, HTTPS only `S`
- Secure token storage on mobile `S`
- AI security review of the auth and webhook code `S`
- Sanity checks on train speed and position jumps; per-train device keys `V2`
- Signed display bundles `V1`
- Front Door with WAF and bot protection; Private Link `V1`
- Defender for Cloud and Sentinel `V1`
- mTLS to partners; certificate pinning; WAF `S` (documented only)
- Pen test; restore drill `S` (documented only)
- Images signed, with admission control `V1`
- AI guardrails: Prompt Shields, Content Safety, no personal data sent to the model `V1`

---

## Observability and operations

- Platform operable in production `B`
- OpenTelemetry to App Insights with a cap `S` `V2` `V1`
- Correlation ID in ProblemDetails, with "Reference: …" shown to users `S`
- Traces that cross the catering boundary and the message bus `S` `V1`
- `/health/live` and `/health/ready`; caterer reported as Degraded `S`
- Business metrics `S`:
  - feed staleness
  - webhook rejections
  - outbox backlog
  - display last-seen
- Alert when there are no observations for 5 min `S`
- Alerts on API errors, stale data and a growing dead-letter queue `V2`
- Runbook `S` `V1`
- SLOs:
  - 3 SLOs as documents `S`
  - propagation ≤ 30 s `S`
  - burn-rate alerts `V1`
- Synthetic probes that check the data is correct, not just that the service is up `V1`
- Prometheus and Grafana `V1`
- Azure Data Explorer for telemetry and analytics `V1`
- Budget alerts `S`
- Cost model for Azure credit and after-credit costs `N`
- .NET Aspire dashboard locally `V2` `C` — **selected for the pilot 14 Oct**

---

## Data and privacy

- Classification of public, passenger, operational, partner-shared and secret data `B` `S` `V2` `V1`
- Retention periods per class `B` `S` `V2` `V1`
- Never log bodies, auth headers, email, phone, names or tokens `S` `V2` `V1`
- Test that fails if an email or phone number appears in the logs `S` `V2`
- Users logged by a random ID, not their email `V2`
- Minimise what the catering partner receives `B` `S`
- Role-based visibility of data `B` `S`
- Reference Sri Lanka's PDPA No. 9 of 2022 `S`
- Data access and erasure handled as scheduled jobs `V1`
- Assistance bookings stored separately `V1`
- Free-text PII redaction `V1`

---

## Quality assurance

- Test strategy covering the full spectrum of quality `B` `S`
- Domain unit tests with an injected clock `S`
- API integration tests against a real Postgres (Testcontainers) `S` `V2` `V1`
- Catering contract test using a shared schema and signed fixtures `S`
- Consumer-driven contract tests `V1`
- Shared freshness test vectors run in C#, Dart and TypeScript `S`
- Flutter widget tests with accessibility checks `S`
- Vitest tests for the display components `S`
- Five automated E2E flows in Playwright `B` `S`
- Flutter `integration_test` for E2E flow 1 `S`
- Maestro on a device farm `C`
- Mutation spot-check `S`
- Requirement IDs in test names, generating a traceability matrix `S`
- Defect log with an origin column `S`
- Playwright HTML report and CI evidence snapshots `S`
- Quality assessment using an ISO 25010 scorecard `S`
- Load testing with k6 `V2` `V1`
- Chaos testing and game days `V1`
- Device matrix `S`
- GTFS conformance tests `V1`
- Coverage reported but not enforced `S`; coverage gates on the domain layer `V1`

---

## UX and design system

- Design language shared across apps `B` `S`
- `tokens.json` and `status.json` turned into CSS and Dart by a script `S`
- Shared `StatusBadge` and `FreshnessLine` components `S`
- 8 status states, each with an icon and a label in three languages `S`
- Different density and scale per surface `S`
- Brand wordmark `C`
- Wireframes for every surface `B` `S`
- Hi-fi screens `B` `S`
- User flows `B` `S`
- Design-system definition document `B` `S`
- Dev-only gallery of every status state, with screenshot tests `C`
- Token contrast script `S`
- WCAG 2.2 AA throughout, 7:1 contrast on displays and the driver app `S` `V1`
- Busy-station layout rules `B` `S`
- 5-second tests, hallway tests, a display glance test at 4 m `S`
- Native-reader review of Sinhala and Tamil text `S`
- Noto fonts bundled `S`
- Figma variable sync and Code Connect `C` (rejected)

---

## Platform, infrastructure and delivery

- Monorepo `N` `S` `V2`
- Modular monolith in .NET 10 `S`; .NET 8 `V2`; Node/NestJS microservices on AKS `V1`
- Docker Compose for local development and the demo fallback `S` — **superseded by Aspire AppHost decision**
- .NET Aspire AppHost `V2` — **selected for local development and demo fallback 14 Oct**
- Azure Container Apps `S` `V2`
- AKS with Argo CD GitOps `V1`
- Azure Static Web Apps `S` `V2`
- PostgreSQL Flexible Server `S` `V2` `V1`
- Redis `V2` `V1`
- Service Bus `V2` `V1`
- Blob storage / ADLS `V2` `V1`
- CDN and Front Door `V1`
- API Management `V1`
- Three environments (dev, staging, prod) `V2`
- Infrastructure as code: Bicep `V2` `V1`; Terraform `V1`
- GitHub Actions CI/CD `S` `V2` `V1`
- Container registry: GHCR `S`; ACR `V2`
- Approval gate before production `V2`
- Canary releases with rollback `V2` `V1`
- Preview URL per pull request `V2`
- Mobile build pipeline `V2`
- Demo reset script `S`
- Backup demo video `S`
- Delete Azure resources after 20 Oct `S`

---

## AI in the product

- Grounded disruption explainer `N`
- Passenger AI chatbot `N`
- Notice drafter for ops, with human approval `V2` `V1` `C`
- Cause-code suggestion `V1`
- Board summarisation and triage `V1`
- Passenger copilot `V1`
- AI Search retrieval index over rules and runbooks `V1`
- Content Safety moderation `V1`
- OpenRouter or Azure OpenAI as the model provider `N` `V2` `V1`
- Log every AI interaction with prompt and model versions `V1`

---

## Assessment deliverables (not product features, listed for completeness)

These are required by brief §23.

### Requirements

- Refined requirements
- Assumptions
- Personas
- User journeys
- User stories
- Acceptance criteria
- Edge cases

### Product and UX

- User flows
- Wireframes
- Hi-fi designs
- Design system
- Accessibility considerations

### Architecture

- High-level architecture
- Application boundaries
- Major components
- Data ownership
- Integration architecture
- Auth approach
- ADRs and trade-offs

### Backend

- Domain model
- APIs
- Persistence
- Validation
- Authorization
- Integration
- Error handling
- Observability

### QA

- Strategy
- Scenarios
- 5 automated E2E flows
- Defects
- Evidence
- Quality assessment

### Security

- Threat model
- Decisions
- Risks
- Controls
- Security-testing evidence

### AI usage

- Tools used
- Responsibilities given to them
- Prompts
- How output was validated
- Where AI was wrong
- Where your judgement improved the result
- Code you modified significantly

### Final demo

- A story told to stakeholders
- Discoveries
- Assumptions
- Exclusions
- Prioritisation
- Architecture, UX, security and QA decisions
- AI's contribution and rejections
- Future improvements

### Other project artefacts

- Evidence register and research protocol `N`
- Stakeholder and actor map `N`
- Service blueprint `N`
- Scenario catalogue `N`
- Domain glossary and state diagrams `N`
- Source-of-truth matrix `N`
- Context, container, event-flow, deployment, trust-boundary and offline diagrams `N`
- Azure cost model `N`
- Demo recovery plan `N`
- Daily tracker and AI log `.tmp/tracker`
