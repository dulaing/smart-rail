# Railway operations web app features

**Goal:** Help staff see what is happening on the pilot corridor, handle incidents, and send the same confirmed service change to passengers, stations, and drivers.

## Build first, in this order

1. **See the pilot trains:** Show each train's last reported station, update time, delay and status. Say **unknown** when a train observation is too old.
2. **Publish confirmed changes:** Authorised staff enter delays, cancellations, platform changes and **“Train ends at [station]”**. Preview affected stops, passenger views, station screens and food orders before publishing.
3. **Handle an early ending:** Keep valid stops before the new endpoint. Remove later arrival estimates, stop new food orders beyond it and cancel existing orders beyond it with a clear reason.
4. **Read and acknowledge driver reports:** Show reports in a Control inbox. A named controller actively acknowledges a report; the driver then sees who acknowledged it and when. Open or resolve a simple incident separately.
5. **Prevent silent overwrites:** If two staff members edit the same change, reject the later save and ask them to reload and review it. The planned API response is **409**. Demonstrate this before claiming it works.
6. **Keep roles separate:** Controllers, station staff, auditors and system administrators have different permissions. Show the change history, system health and simulator controls to authorised staff.

## Later

Paper-versus-app conflict handling, maintenance routing, network-wide tracking, crew and engine planning, train-control charts, crossing registers, ticketing, freight and advanced analytics. Formal incident classes, deadlines and reference numbers need verified SLR rules before we use them.

## Important limits

- This prototype uses **simulated train and operational data**. It does not control trains or issue safety-critical orders.
- The shared response mentions “SLR-OMS,” L0–L6 roles, paper-first rules, and exact escalation times. We could not verify those details in public SLR material, so they are **questions to confirm**, not rules we claim to implement.
- The assessment still requires a passenger app, even though the response suggests deferring one.
