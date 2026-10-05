# Railway operations web app features

**Goal:** Help staff see what is happening on the pilot corridor, handle incidents, and send the same confirmed service change to passengers, stations, and drivers.

## Build first, in this order

1. **Handle incidents:** Report a problem, choose a provisional severity, assign an owner, record actions, and close it. Show who changed it and when.
2. **See the corridor:** Show each pilot train, its last reported location and update time, delays, cancellations, and affected stations. Say **unknown** when train data is stale.
3. **Confirm service changes:** Authorised staff enter delays, cancellations, and platform changes. Show the affected journeys before publishing updates to the other apps.
4. **Use clear permissions:** Separate controller, station, maintenance, manager, system administrator, and auditor access. Staff see and change only what their job requires; an IT administrator cannot edit train status. Temporary access has an end date and a recorded authoriser.
5. **Handle conflicting records:** If staff report that a paper record and the app disagree, flag the conflict and record how it was resolved. The app must not silently overwrite either record.
6. **Receive field reports:** See driver messages and engine defects, then route them to the right operational or maintenance staff.

## Later

Network-wide tracking, crew and engine planning, train-control charts, crossing registers, ticketing, freight, and advanced analytics. Formal incident classes, deadlines, and reference numbers need verified SLR rules before we use them.

## Important limits

- This prototype uses **simulated train and operational data**. It does not control trains or issue safety-critical orders.
- The shared response mentions “SLR-OMS,” L0–L6 roles, paper-first rules, and exact escalation times. We could not verify those details in public SLR material, so they are **questions to confirm**, not rules we claim to implement.
- The assessment still requires a passenger app, even though the response suggests deferring one.
