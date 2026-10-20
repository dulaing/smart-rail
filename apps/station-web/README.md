# SmartRail station boards

Passive, Light-only entrance and platform displays. Public timetable/running/change reads need
no login. These are fictional SmartRail simulations, not a railway control system.

## Run locally

From the repository root, with the Aspire API and seeded timetable available:

```powershell
npm --prefix apps/station-web ci
npm --prefix apps/station-web run dev
```

The default origin is `http://localhost:5173` and API is `https://localhost:7074`.
Only one app can use port 5173 at a time. For another origin, include that exact origin in the
API's `Cors:AllowedOrigins` settings and restart the API. Keep the local development CA trusted.

| Address | Display |
| --- | --- |
| `/?station=RGM&view=entrance` | Ragama entrance/waiting-area overview |
| `/?station=RGM&view=platform&platform=1` | Ragama platform 1 |
| `/?station=FOT&view=entrance` | Colombo Fort entrance |

`station` accepts a station code or ID. `view` defaults to `entrance`; a platform view requires
`platform`. Unknown stations/platforms show a configuration error. Build-time defaults are
`VITE_STATION`, `VITE_VIEW`, `VITE_PLATFORM`, and `VITE_API_URL`.

## Production and offline verification

```powershell
npm --prefix apps/station-web run build
npm --prefix apps/station-web run preview -- --port 4173 --strictPort
```

Add `http://localhost:4173` to the API's allowed origins for that preview. A deployment that
proxies `/api/` to SmartRail on the board's own origin can build with `VITE_API_URL=/` instead.
The proxy must forward ETags and `X-SmartRail-Server-Time` unchanged.

The service worker is registered only in production, on HTTPS or localhost. It caches the app
shell and all three bundled fonts. A versioned localStorage entry, `smartrail.station-board`,
stores only public station names and dated timetables, never live estimates, effective platform
overrides, confirmed changes, identities or tokens.

1. Open a production board online; wait for rows and a controlling service worker.
2. Switch the browser offline and reload. The saved timetable, clock and simulation footer
   remain; one offline banner appears, with no cached ETA or effective platform override.
3. Clear that localStorage entry while retaining the app-shell cache, then reload offline.
   The no-timetable message, clock and footer remain.
4. Reconnect. The board refreshes automatically. Observation age still comes from the original
   observation time, including through ETag 304 responses.

The 1920x1080 entrance board fits complete rows without reducing the station typography.
Confirmed disruptions take priority over ordinary services; visible rows retain upcoming-time
order. A trilingual count identifies additional services. All three languages remain together,
with no carousel or ticker. Physical four-metre/sunlight checks and native Sinhala/Tamil review
still require people on site; digital tests cannot establish those results.

## Checks

```powershell
npm --prefix apps/station-web test
npm --prefix apps/station-web run lint
npm --prefix apps/station-web run build
npm --prefix apps/station-web run visual
```

`visual` runs the 1920 x 1080 Figma comparisons, the contrast/clipping/language checks and the
offline/reconnect smoke test on the production build (it builds and serves it itself). Update the
goldens with `npm --prefix apps/station-web run visual -- --update-snapshots` only after reviewing
the new captures against the approved frames.

See `_specs/station-boards.md` for browser evidence and remaining human checks.