# Station terminal features

**Goal:** Help someone at the station answer: **“Is my train coming, and which platform?”** This is a public touchscreen, separate from the passive station displays.

## Build first, in this order

1. **My train now:** Show the next trains from this station, expected arrival or departure, platform, and status. Let people find a train by destination or departure time. Show when each update was received.
2. **Clear changes:** Put cancellations, long delays, and platform changes where people will see them immediately.
3. **Equal access:** Make Sinhala, Tamil, and English easy to choose from the first screen. Use large, high-contrast text. Choose a device with audio output and an accessible way to start spoken guidance.
4. **Honest offline mode:** If the network fails, show the saved timetable and say **“Live updates unavailable”** with the last update time. Recover automatically when the connection returns. Never show an old ETA as current.
5. **Station notices:** Show urgent staff-confirmed notices prominently. The terminal does **not** replace the station's PA or emergency procedures.

## Physical and upkeep needs

Place the screen where wheelchair users can reach it. Test readability in sunlight and at night. Give station staff a simple way to report a fault and restart the terminal. A power cut needs backup power to keep it running; software alone cannot solve that.

## Later

Full route planning, seat booking, catering browsing, and other passenger services. They should not crowd the first screen.

**Demo truth:** SmartRail has no real train feed. Train movement and operational updates shown here are clearly labelled **simulated**; published timetable times remain separate.
