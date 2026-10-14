# Passenger app features

**Goal:** Help Kasun decide, before leaving home or while waiting at Gampaha, whether to wait for the train or take another option. His screen feedback sets the first priorities; we will test them with more passengers.

## Build first, in this order

1. **First screen, no account needed:** Start with **Gampaha → Colombo Fort** for the commuter demo and let anyone change the two stations. Show the next **two or three trains** with scheduled and estimated arrival at Gampaha, delay, boarding platform, and when each update was received. Highlight the train expected to arrive at Colombo Fort soonest only when its estimate is current, it stops at both stations, and it is not cancelled. Otherwise, make no recommendation.
2. **Clear changes where I board:** Put a cancellation, early ending or platform change at **Gampaha** above the train list. Show the old and new platform, who confirmed the change, and when. Changes at later stations remain on the train detail screen. If a train now ends at an earlier station, say **“Train ends at [station]”** and do not suggest stops beyond it.
3. **Honest train detail:** Show whether a train is on time, late or cancelled; its scheduled and estimated arrival separately; its last known station; its stops; and a small simulated route map. After **4 minutes without a new train observation**, show “Current arrival unknown — last seen X minutes ago at [station]” and hide the train marker.
4. **Follow and alerts:** A small **Follow** action can ask for demo-account sign-in. A signed-in passenger can save the commute and receive relevant in-app alerts while the app is open. Everyone can see public disruption notices for the route they are viewing.
5. **Everyday access:** Sinhala, Tamil and English; clear **Current simulated updates** or **Saved timetable / offline** labels; an offline timetable and saved information marked with its age. A confirmed platform change stays visible with its own confirmation time even if the train position becomes unknown.

The app opens straight to the train list. It should answer **“What is my next train to Fort, when will it reach Gampaha, which platform do I board from, and how recent is this information?”** in a quick glance. If someone opens sign-in for a personal action, that screen has a clear **Continue without an account** option back to the train list. Food stays in its own tab.

## Smaller features for the assessment

- Search another route when planning ahead and view a train's stops.
- Browse food in its own tab. Sign in with a seeded demo account to order and view a pickup code and order status. There is no passenger sign-up flow in this pilot.

## Later

Crowding reports, season tickets, seat reservations, coach stopping positions, and advanced connection planning. These need reliable data or railway cooperation. Crowding is **out of this build**, as agreed.

## Rules and next step

- Authorised operational staff enter and confirm simulated delays, cancellations, and platform changes. Record who changed what and when.
- Train position and ETA start visibly ageing after 3 minutes and become **unknown at 4 minutes**. A confirmed cancellation or platform change stays visible until staff change it.
- If a train ends early, keep the stops it still serves, remove estimates for stops it will not reach, and stop new food orders beyond the new final station. Existing orders beyond it are cancelled with a clear reason. The pilot takes payment at collection, so it has no refund flow.
- Public train and disruption information is readable without sign-in. Saved commutes, personal alerts and food orders require a seeded demo account. No account is needed simply to check a train.
- At 4 minutes without a position, keep the route and stations visible but remove the train marker. Bring it back only after a new position arrives. The map uses the existing train data and downloads no map tiles.
- Build the first screen and alerts, then test them with commuters and Sinhala and Tamil readers.
- Use the examples in [pilot train examples](pilot-services.md). This project has **no real live train feed**; show “SmartRail simulation” for simulated updates.
