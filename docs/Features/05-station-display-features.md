# Station display features

**Goal:** Let someone glance up and know **which train, which platform, and what changed**. These screens are passive; the station terminal handles searching.

## Each screen has one job

- **Platform:** Show this platform's next train, destination, expected time, and status. Keep it visible; no news, ads, or rotating pages.
- **Entrance:** Show an overview of upcoming departures, arrivals, platforms, and major disruptions.
- **Waiting area:** Show the overview plus a short explanation of delays or cancellations.

## Build first

1. **Clear changes:** Make cancellations, delays, and platform changes stand out with words and strong contrast, not colour alone. Avoid flashing text.
2. **Three languages at once:** Show Sinhala, Tamil, and English together for key status and disruption messages. Do not make passengers wait for a language carousel.
3. **Honest updates:** The operations app sends confirmed changes automatically. Show the source and last update time. A station staff correction must be recorded and visible to the operations team.
4. **Network failure:** Keep a downloaded timetable visible with a prominent **“Live updates unavailable”** message and timestamp. Do not show an old ETA or platform as if it is current. Recover when connected.
5. **Urgent notices:** Authorised station staff can put a clear message on affected screens. This supports, but does not replace, the PA and established emergency procedures.

## Physical upkeep

Test screens from a distance, in sunlight, and over a crowd. Monitor failures; give local staff a way to report faults and restart a screen. Plan cleaning and maintenance. A power cut requires backup power; software cannot keep an unpowered screen on.

**Demo truth:** Train movement and changes are **SmartRail simulations**, not live Sri Lanka Railways feeds. Keep published timetable times separate.
