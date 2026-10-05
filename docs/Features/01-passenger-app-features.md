# Passenger app features

**Goal:** Help Kasun decide, before leaving home or while waiting at Gampaha, whether his train is coming and what to do next. His feedback sets the first priorities; we will test the app with more passengers.

## Build first, in this order

1. **Train status and arrival:** Show whether a saved train is delayed or cancelled, its last known station, and its estimated arrival. Keep the scheduled time separate. After **4 minutes without a new train observation**, show “Current arrival unknown — last seen X minutes ago at [station].”
2. **Disruptions:** Clearly show cancellations, delays, affected stations, and the next option when known.
3. **Platform and time changes:** Alert passengers following the train. Show who confirmed the change and when.
4. **Next trains:** Let passengers compare the next few useful trains between two stations.
5. **Everyday access:** Sinhala, Tamil, and English; a low-data view; offline timetable and saved journeys. Old updates must show their age.

The home screen should answer **“Is my train coming?”** immediately.

## Smaller features for the assessment

- Search for a journey and view its stops.
- View food options and arrange collection through the separate simulated catering provider.
- Show a train on a map only when its position is recent, and label simulated movement clearly.

## Later

Crowding reports, season tickets, seat reservations, coach stopping positions, and advanced connection planning. These need reliable data or railway cooperation. Crowding is **out of this build**, as agreed.

## Rules and next step

- Authorised operational staff enter and confirm simulated delays, cancellations, and platform changes. Record who changed what and when.
- Train position and ETA start visibly ageing after 3 minutes and become **unknown at 4 minutes**. A confirmed cancellation or platform change stays visible until staff change it.
- Build the first screen and alerts, then test them with commuters and Sinhala and Tamil readers.
- Use the examples in [pilot train examples](pilot-services.md). This project has **no real live train feed**; show “SmartRail simulation” for simulated updates.
