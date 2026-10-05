# Driver / operator app features

**Goal:** Help a driver prepare for a journey and report problems to control. This priority comes from one driver's account; we must check the workflow with more railway staff.

## Build first, in this order

1. **Briefing before departure:** Show the assigned journey, train or engine, temporary speed restrictions, closures, and hazards ahead. Show who issued each item and when. In our demo, these are **simulated**.
2. **Messages with control:** Staff can send a short, timestamped report; control can reply. Urgent safety communication still follows the railway's established radio and emergency procedures.
3. **Engine condition:** Show the assigned engine, known defects, fuel and last service when those records exist. Let staff report a new defect, with a photo when safe to take one, to operations or maintenance.
4. **Delay reasons:** Record a simple reason such as signal failure, engine defect, restriction, or flooding, so delays have context.
5. **Offline access:** Download the briefing before departure. Show its last update time; queue non-urgent reports until connected. Never imply that an old briefing includes new restrictions.

Use large, high-contrast text that works in sunlight. Design reports for use **while stopped**, not while driving.

## Later, with railway approval

Crossing instructions, single-line movement orders, and any app that drivers rely on for actual speed or movement authority. These need the railway's rulebook, authorised data, safety testing, and operational approval. We will not simulate them as if they were valid orders.

## Clear boundaries

- The demo app **does not issue driving instructions**. Actual signals, authorised orders, and railway procedures take precedence. We need to confirm the exact authority rules with Sri Lanka Railways.
- No passenger ticketing or crowding features. No minute-by-minute driver surveillance feature.
- A device without a connection may show its saved briefing, but must say when it was last checked. For urgent changes, staff use established control procedures.

[RSSB's guidance on mobile devices in driving cabs](https://www.rssb.co.uk/standards-catalogue/CatalogueItem/cop0129-iss-3) helped frame the distraction risk; it is UK guidance, not a Sri Lanka Railways operating rule.
