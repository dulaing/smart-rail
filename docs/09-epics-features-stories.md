# SmartRail epics, features and user stories

Each epic is a big goal. Each bullet is a feature with one user story. **Must** is pilot scope; **Should** follows if the core works. Train movement is simulated, not a live railway feed.

## 1. Help passengers decide which train to take

- **Next trains (Must).** As a passenger, I want to open Gampaha → Colombo Fort without an account and see the next two or three trains, with scheduled and expected times, boarding platforms and update times, so I can decide whether to wait.
- **Train details (Must).** As a passenger, I want to change stations and see a train's stops, delay and last known station. After four minutes without an update, its position and arrival estimate must say **unknown**, so I do not trust an old estimate.
- **Changes and alerts (Must).** As a passenger, I want to see cancellations, early endings and boarding-platform changes. If I follow a train with a demo account, I want an alert while the app is open, so I can act in time.
- **Three languages (Must).** As a passenger, I want key updates in Sinhala, Tamil or English, so I can read them quickly.
- **Saved information (Should).** As a passenger, I want the downloaded timetable and saved train information offline, with their age shown, so I know how old they are.
- **Route diagram (Should).** As a passenger, I want a small diagram of the train's stops, so I can follow its route. A moving marker is optional and disappears when its position is old.

## 2. Let railway staff publish one clear service change

- **Train overview (Must).** As a controller, I want each pilot train's status, last station, delay and update time in one view, so I can spot problems.
- **Publish changes (Must).** As an authorised staff member, I want to preview and publish delays, cancellations, platform changes and early endings, so affected stops, screens and food orders are handled together.
- **Driver reports and incidents (Must).** As a controller, I want to read and acknowledge driver reports, then open or resolve an incident, so the driver and team know what happened.
- **Edit protection (Must).** As a controller, I want a warning if someone updated the same change while I was editing, so I review it before saving.

## 3. Help drivers stay informed and report problems

- **Saved briefing (Must).** As a driver, I want my assigned stops, times and confirmed changes saved offline, with the sender and download time, so I can read them while disconnected.
- **Report and acknowledgement (Must).** As a driver, I want to send a short reason to Control and see **Not sent**, **Sent — awaiting Control**, or **Acknowledged by [name]**, so I know whether Control acted on it.
- **Send later (Should).** As a driver, I want to save a non-urgent report offline and send it when connected, so it is not lost. Until then, it says **Not sent**.

## 4. Show clear information at stations

- **Train boards (Must).** As a station passenger, I want the platform's next train and the entrance's upcoming trains, with changes, delay reasons and update times in Sinhala, Tamil and English together, so I find the right place to wait.
- **Offline board (Must).** As a station passenger, I want a clock, saved timetable and **Live updates unavailable** message when disconnected, so old information is not shown as current.

## 5. Let passengers collect food at a station

- **Order for pickup (Must).** As a passenger, I want to use my demo account to choose food and a collection station, then get a pickup code, so I can collect and pay at the kiosk.
- **Kiosk orders (Must).** As kiosk staff, I want to mark orders **Ready**, **Collected** or **No-show**, so passengers know their status.
- **Train changes (Must).** As a passenger, I want my pickup time updated after a delay, or my order cancelled with a reason if the train cannot reach my collection station, so I do not make a wasted trip.

## 6. Make the pilot safe to run and honest to demonstrate

- **Train simulation (Must).** As a demo operator, I want to pause and resume simulated updates and make RailBites temporarily unavailable, so I can demonstrate failures and recovery.
- **Access and history (Must).** As a railway manager, I want public train information open to guests, sign-in through Keycloak, staff changes restricted by SmartRail's station and journey permissions, and every change recorded, so actions are accountable.
- **Reliable updates (Must).** As operations staff, I want confirmed changes to reach all apps and RailBites, even after a brief outage, so everyone sees the same decision.
- **Health and tests (Must).** As the demo operator, I want health checks, local Aspire telemetry and tests of the main journeys, so I can notice failures and show what works.
- **Azure demo (Should).** As an assessor, I want to try the pilot online, so I can review it without installing anything. A local demo remains available if deployment is not ready.

**Outside this pilot:** ticket payments, background phone notifications, real train tracking, driver safety orders, product AI and full railway-wide operations.
