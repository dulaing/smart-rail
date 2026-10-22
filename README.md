# SmartRail

SmartRail is my project for the **Individual Cross-Practice AI-Assisted Software Development Assessment**.
The brief was a fictional national railway that wants one digital platform for passengers, drivers,
stations, central operations and outside service providers. The brief was only a starting point,
so part of the work was finding the missing rules, cutting the scope and justifying the decisions.

I built a small pilot for one corridor (Colombo Fort, Gampaha, Veyangoda, Polgahawela, Peradeniya)
in 20 days. The idea in one line:

> A railway change should be confirmed once, reach everyone affected, and never make old information look current.

Train movement is **simulated** and every screen says so. When updates stop, screens show
**Unknown** instead of pretending the train is on time.

## What is in the repo

| Part | Tech | Folder |
| --- | --- | --- |
| Backend API | .NET 10, PostgreSQL, EF Core | `services/api` |
| Local orchestration | .NET Aspire (Postgres, Keycloak, API, Grilled) | `services/apphost` |
| Sign-in | Keycloak (OIDC, PKCE for mobile) | `services/apphost/realms` |
| Passenger app | Flutter (Android) | `apps/passenger_app` |
| Driver app | Flutter (Android tablet) | `apps/driver_app` |
| Operations web | React + Vite | `apps/ops-web` |
| Station boards | React + Vite (kiosk screens) | `apps/station-web` |
| Grilled (food provider) | Node + TypeScript + SQLite, separate service | `services/grilled` |
| Shared Flutter code | Dart package | `packages/smartrail_core` |
| Tests | xUnit API tests, Playwright journeys, Flutter widget tests | `tests/`, `apps/*/test` |

Specs for each feature live in `_specs/`. Scope, decisions and architecture are in `docs/`.

## Screenshots

All screenshots are from the apps running locally on 22 Oct 2026.

### Passenger app (Pixel 6a)

| Home | Trains | Train details |
| --- | --- | --- |
| <img src="docs/screenshots/passenger-01-home.png" width="240"> | <img src="docs/screenshots/passenger-02-trains.png" width="240"> | <img src="docs/screenshots/passenger-03-train-detail.png" width="240"> |
| **Food orders** | **Account** | **Home in Sinhala** |
| <img src="docs/screenshots/passenger-04-food.png" width="240"> | <img src="docs/screenshots/passenger-05-account.png" width="240"> | <img src="docs/screenshots/passenger-06-home-sinhala.png" width="240"> |

### Driver app (Pixel Tablet)

| Today's duty | Briefing |
| --- | --- |
| <img src="docs/screenshots/driver-01-duty.png" width="420"> | <img src="docs/screenshots/driver-02-briefing.png" width="420"> |
| **Report a problem** | **My reports and acknowledgements** |
| <img src="docs/screenshots/driver-03-report-problem.png" width="420"> | <img src="docs/screenshots/driver-04-my-reports.png" width="420"> |

### Operations web (Control staff)

| Corridor | Train details |
| --- | --- |
| <img src="docs/screenshots/ops-01-corridor.png" width="420"> | <img src="docs/screenshots/ops-02-journey-detail.png" width="420"> |
| **Compose a change** | **Driver report inbox** |
| <img src="docs/screenshots/ops-03-compose-change.png" width="420"> | <img src="docs/screenshots/ops-04-driver-report.png" width="420"> |
| **Incidents** | **Audit history (Auditor, read only)** |
| <img src="docs/screenshots/ops-05-incidents.png" width="420"> | <img src="docs/screenshots/ops-06-audit-history.png" width="420"> |

### Station boards (1920 x 1080, Sinhala, Tamil and English together)

| Entrance board | Platform board |
| --- | --- |
| <img src="docs/screenshots/station-01-peradeniya-entrance.png" width="420"> | <img src="docs/screenshots/station-02-peradeniya-platform-1.png" width="420"> |

### Grilled kiosk and API

| Kiosk staff sign-in | Kiosk orders | Swagger (API docs) |
| --- | --- | --- |
| <img src="docs/screenshots/grilled-01-kiosk-login.png" width="280"> | <img src="docs/screenshots/grilled-02-kiosk-orders.png" width="280"> | <img src="docs/screenshots/api-01-swagger.png" width="280"> |

## How to run it locally

You need Docker, the .NET 10 SDK, Node 24 and Flutter.

```
dotnet run --project services/apphost        # Postgres, Keycloak, API, Grilled + Aspire dashboard
npm --prefix apps/ops-web run dev            # Operations web on http://localhost:5173
npm --prefix apps/station-web run dev        # Station boards on http://localhost:5174
cd apps/passenger_app && flutter run         # or apps/driver_app
```

Swagger is at `https://localhost:7074/swagger` (also linked from the Aspire dashboard).
Demo users are `demo-passenger`, `demo-driver`, `demo-controller`, `demo-station-gph`,
`demo-auditor`, `demo-sysadmin` and `demo-simoperator`. Certificates, Android setup and seeding
are explained in [services/README.md](services/README.md).

## What is left

Still to do:

- **Demo controls page** in Operations (pause/resume a train, switch Grilled off and on). The backend
  works but the page still says "Not available yet".
- **Save a commute and follow a train** in the passenger app. The API is done, the screens are not.
- **Driver offline briefing.** Reopening a saved briefing offline is only partly done.
- **End-to-end tests.** The five Playwright journeys are written but the final full run is not done.
- **Mobile checks on real devices**: an automated Flutter integration test, TalkBack and large text.
- **Figma match** for the Driver app and Operations is still in progress, and the Grilled kiosk is not
  restyled yet.
- **Cloud deployment.** Everything runs locally with Aspire. Nothing is deployed to Azure yet.
- **Small bugs I found while taking these screenshots**: the route title on the Trains tab does not
  update when the From station changes. The Grilled kiosk login also needs `GRILLED_KIOSK_STAFF` set
  by hand because Aspire does not set it.

Left out on purpose for this pilot: real GPS train positions, online payment, push notifications,
ticketing, an interactive station terminal, product AI features and an iOS build.
