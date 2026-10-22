# Grilled provider service

Grilled is the separate Node/TypeScript service that owns collection points, menus, stock, pickup
codes, order state and the durable callback outbox. The machine API boundary is described in
`openapi.yaml`.

## Staff page

Staff at one collection point can view that point's orders and mark them Ready, Collected (with the
customer's pickup code) or No-show. The page is served by this same process:

- `GET /staff/login` — sign in with the collection point's shared staff password.
- `GET /staff` — the point's orders, polled every 15 seconds while the tab is visible.
- `POST /staff/login`, `POST /staff/logout` and the small order actions are same-origin, session
  protected and require a CSRF token.

The browser holds only an opaque session id in an HttpOnly, SameSite=Strict cookie. Sessions live in
memory, so restarting the service signs everyone out. The collection point is fixed when the session
is created; a form or URL cannot widen it. Internal keys (`GRILLED_KIOSK_KEYS`, the service and
admin keys) never reach the browser.

## Provisioning the shared staff login

One password per collection point, stored only as a salted scrypt hash. Generate an entry without
writing the password anywhere but standard input:

```
printf '%s' "$PASSWORD" | npx tsx scripts/hash-staff-password.ts --point polgahawela-junction
```

Put the printed `point=hash` value into `GRILLED_KIOSK_STAFF`, joining multiple points with commas.
Do not commit the value; use the environment (or the matching Aspire secret).

| Variable | Purpose | Default |
| --- | --- | --- |
| `GRILLED_KIOSK_STAFF` | `point-id=scrypt-hash` entries; empty disables the staff login | empty |
| `GRILLED_KIOSK_SESSION_MINUTES` | Staff session lifetime in minutes | 60 |
| `GRILLED_KIOSK_LOGIN_MAX_ATTEMPTS` | Attempts allowed per address per window | 10 |
| `GRILLED_KIOSK_LOGIN_WINDOW_MS` | Rate-limit window length | 300000 |
| `GRILLED_KIOSK_INSECURE_COOKIES` | `1` drops `Secure` from the session cookie. Ignored when `NODE_ENV=production` | unset |

Cookies are `Secure` by default. The only way to drop it is the explicit
`GRILLED_KIOSK_INSECURE_COOKIES=1` local-development flag, and it has no effect in production.

## Orders shown to staff

The kiosk view never includes `pickupCode`. The staff page shows the train number and service date,
the pickup stop, items and quantities, the scheduled versus last-reported pickup time with its
evidence age, and any cancellation reason.
