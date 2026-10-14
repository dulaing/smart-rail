# Station display features

**Goal:** Let someone glance up and know **which train, which platform, and what changed**. These screens are passive; the station terminal handles searching.

## Each screen has one job

- **Platform:** Show this platform's next train, destination, expected time, and status. Keep it visible; no news, ads, or rotating pages.
- **Entrance:** Show an overview of upcoming departures, arrivals, platforms, and major disruptions.
- **Waiting area:** Reuse the entrance overview with a short delay explanation; no separate app is needed.

## Build first

1. **Clear changes:** Make cancellations, delays, platform changes and **“Train ends at [station]”** stand out with words and strong contrast, not colour alone. If a train ends early, do not show it as going to later stations. Avoid flashing text.
2. **Three languages at once:** Show Sinhala, Tamil, and English together for key status and disruption messages. Do not make passengers wait for a language carousel.
3. **Honest updates:** Show the time of the last train observation and the time of each confirmed change. An old position becomes **unknown** after four minutes; a separately confirmed platform change remains visible with its own time. Changes come from the authorised operations workflow.
4. **Network failure:** Keep a downloaded timetable visible with a prominent **“Live updates unavailable”** message and timestamp. Do not show an old ETA or platform as if it is current. Recover when connected.

Free-text urgent notices and direct screen corrections are later work. They need a verified staff workflow; this pilot uses the operations app for confirmed service changes.

## Physical upkeep

Test screens from a distance, in sunlight, and over a crowd. Monitor failures; give local staff a way to report faults and restart a screen. Plan cleaning and maintenance. A power cut requires backup power; software cannot keep an unpowered screen on.

**Demo truth:** Train movement and changes are **SmartRail simulations**, not live Sri Lanka Railways feeds. Keep published timetable times separate.
