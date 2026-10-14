# Driver / operator app features

**Goal:** Help a driver read the assigned journey and report a problem to Control. The driver account below is input for the pilot, not proof of official railway procedure.

## Build first, in this order

1. **Journey briefing:** Show the assigned train, stops, times and confirmed changes. Show who sent each change and when.
2. **Report a problem:** Pick a short reason such as train fault, signal delay or obstruction. Show when the report was sent.
3. **Honest acknowledgement:** Show **Not sent** if the report has not reached the server; **Sent — awaiting Control acknowledgement** after delivery; and **Acknowledged by [name] at [time]** only after a controller actively acknowledges it. Delivery alone is not acknowledgement.
4. **Offline briefing:** Keep the last downloaded briefing with its download time. Say it may be old. If the optional offline report queue is cut, clearly say **Not sent — Control has not been told**; never pretend a report was queued or delivered.

Use large, high-contrast text that works in sunlight. Design reports for use **while stopped**, not while driving. Urgent problems use established railway radio and emergency procedures; the app must direct the driver there.

## Later, with railway approval

Temporary speed restrictions, crossing and single-line orders, engine records, defect photos, chat, and any app that drivers rely on for speed or movement authority. These need authorised data, railway rules and safety approval. A non-urgent offline report queue is optional for this pilot.

## Clear boundaries

- The demo app **does not issue driving instructions**. Actual signals, authorised orders, and railway procedures take precedence. We need to confirm the exact authority rules with Sri Lanka Railways.
- No passenger ticketing or crowding features. No minute-by-minute driver surveillance feature.
- A device without a connection may show its saved briefing, but must say when it was last checked. For urgent changes, staff use established control procedures.

[RSSB's guidance on mobile devices in driving cabs](https://www.rssb.co.uk/standards-catalogue/CatalogueItem/cop0129-iss-3) helped frame the distraction risk; it is UK guidance, not a Sri Lanka Railways operating rule.
