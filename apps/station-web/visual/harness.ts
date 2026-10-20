// Shared by the visual and smoke checks: the approved Station states as test scenarios, and the
// routine that opens one with a mocked, deterministic API and a frozen Colombo clock.

import { readFileSync } from 'node:fs'
import type { Page } from '@playwright/test'
import { ageing, cancellation, changesOf, confirmedDelay, crowded, day, endsEarly, failedRead, fortArrivals, gampahaDepartures, journeysResponse, notServed, platformCancelled, platformEndsEarly, platformMoved, platformNext, platformNotStopping, runningOf, savedTimetable, stations, unknownEta, type Train } from './scenarios.ts'


export const out = 'visual/comparisons/2026-10-20'
export const bounds = JSON.parse(readFileSync(`${out}/station.reference-bounds.json`, 'utf8')) as Record<string, Record<string, Record<string, number>>>

export type Scenario = {
  url: string
  /** Colombo wall time on the fixture date. */
  now: string
  trains: Train[]
  /** The saved snapshot the board starts from (offline), downloaded at this Colombo time. */
  saved?: string
  /** Every API read fails. */
  offline?: boolean
  /** No API read ever answers (the first load). */
  hang?: boolean
  ready: string
  /** The approved Figma export this capture is laid over, under visual/references/2026-10-19. */
  reference?: string
}

const entrance = '/?station=GPH&view=entrance'
const platform = (n: number) => `/?station=GPH&view=platform&platform=${n}`

export const scenarios: Record<string, Scenario> = {
  'entrance-normal': { url: entrance, now: '07:08', trains: gampahaDepartures, ready: '.row', reference: 'entrance.png' },
  'platform-next': { url: platform(2), now: '07:20', trains: platformNext, ready: '.service-card', reference: 'platform.png' },
  'entrance-offline': { url: entrance, now: '07:20', trains: savedTimetable, saved: '07:05', offline: true, ready: '.row', reference: 'offline-timetable.png' },
  'entrance-arrivals': { url: '/?station=FOT&view=entrance', now: '08:08', trains: fortArrivals, ready: '.row', reference: 'states/231-178-terminating-arrivals.png' },
  'entrance-cancellation': { url: entrance, now: '07:08', trains: cancellation, ready: '.row', reference: 'states/234-250-cancellation-delay.png' },
  'entrance-unknown': { url: entrance, now: '07:08', trains: unknownEta, ready: '.row', reference: 'states/238-1169-unknown-eta.png' },
  'entrance-ageing': { url: entrance, now: '07:08', trains: ageing, ready: '.row', reference: 'states/238-1042-ageing.png' },
  'entrance-failed-read': { url: entrance, now: '07:08', trains: failedRead, ready: '.row', reference: 'states/238-1297-failed-read.png' },
  'entrance-empty': { url: entrance, now: '22:30', trains: [], ready: '.passive-state', reference: 'states/240-1733-no-upcoming.png' },
  'display-loading': { url: entrance, now: '07:08', trains: [], hang: true, ready: '.passive-state', reference: 'states/240-1704-loading.png' },
  'display-config-error': { url: '/?station=XXX&view=entrance', now: '07:08', trains: [], ready: '.passive-state', reference: 'states/240-1762-config-error.png' },
  'entrance-no-saved': { url: entrance, now: '07:20', trains: [], offline: true, ready: '.passive-state', reference: 'states/239-1533-no-saved-entrance.png' },
  'platform-no-saved': { url: platform(3), now: '07:20', trains: [], offline: true, ready: '.service-empty', reference: 'states/239-1574-no-saved-platform.png' },
  'platform-saved': { url: platform(2), now: '07:20', trains: savedTimetable, saved: '07:05', offline: true, ready: '.service-card', reference: 'states/239-1427-saved-platform.png' },
  'platform-moved': { url: platform(2), now: '07:20', trains: platformMoved, ready: '.service-card', reference: 'states/236-596-platform-moved-from.png' },
  'platform-cancelled': { url: platform(2), now: '07:55', trains: platformCancelled, ready: '.service-card', reference: 'states/236-783-platform-cancelled.png' },
  'entrance-ends-early': { url: entrance, now: '07:08', trains: endsEarly, ready: '.row', reference: 'states/234-357-ends-early.png' },
  'entrance-not-served': { url: '/?station=FOT&view=entrance', now: '08:08', trains: notServed, ready: '.row', reference: 'states/234-544-not-served.png' },
  'entrance-confirmed-delay': { url: entrance, now: '07:08', trains: confirmedDelay, ready: '.row', reference: 'states/234-623-confirmed-delay.png' },
  'platform-ends-early': { url: platform(3), now: '07:20', trains: platformEndsEarly, ready: '.service-card', reference: 'states/236-845-platform-ends-early.png' },
  'platform-not-stopping': { url: '/?station=FOT&view=platform&platform=4', now: '08:08', trains: platformNotStopping, ready: '.service-card', reference: 'states/236-969-platform-not-stopping.png' },
  'entrance-crowded': { url: entrance, now: '07:08', trains: crowded, ready: '.row' },
  'platform-no-next': { url: platform(3), now: '22:30', trains: [], ready: '.service-empty', reference: 'states/236-1093-platform-no-next.png' },
}

/** Opens a scenario; `base` is the origin to load it from (the dev server unless told otherwise). */
export async function open(page: Page, scenario: Scenario, base?: string) {
  const now = new Date(`${day}T${scenario.now}:00+05:30`)
  await page.clock.setFixedTime(now)
  if (scenario.saved) {
    const snapshot = {
      version: 1,
      stations,
      stationsSavedAt: new Date(`${day}T${scenario.saved}:00+05:30`).getTime(),
      days: { [day]: { serviceDate: day, downloadedAt: new Date(`${day}T${scenario.saved}:00+05:30`).getTime(), journeys: journeysResponse(scenario.trains, day).journeys } },
    }
    await page.addInitScript((value) => window.localStorage.setItem('smartrail.station-board', value), JSON.stringify(snapshot))
  }
  await page.route('https://localhost:7074/api/**', async (route) => {
    if (scenario.hang) return new Promise<void>(() => {})
    if (scenario.offline) return route.abort('internetdisconnected')
    const url = new URL(route.request().url())
    const headers = { 'content-type': 'application/json', 'x-smartrail-server-time': now.toISOString(), 'access-control-allow-origin': '*', 'access-control-expose-headers': 'X-SmartRail-Server-Time' }
    const json = (body: unknown) => route.fulfill({ status: 200, headers, body: JSON.stringify(body) })
    if (url.pathname === '/api/stations') return json(stations)
    if (url.pathname === '/api/journeys') return json(journeysResponse(scenario.trains, url.searchParams.get('serviceDate') ?? day))
    const match = /^\/api\/journeys\/([^/]+)\/(running|changes)$/.exec(url.pathname)
    const train = scenario.trains.find((t) => t.id === match?.[1])
    if (!match || !train) return route.fulfill({ status: 404, headers, body: '{}' })
    if (match[2] === 'changes') return json(changesOf(train))
    const running = runningOf(train)
    return running && !train.readFails ? json(running) : route.fulfill({ status: 503, headers, body: '{}' })
  })
  page.on('pageerror', (e) => console.log('PAGEERROR', e.message.slice(0, 400)))
  await page.goto(base ? new URL(scenario.url, base).toString() : scenario.url)
  await page.waitForSelector(scenario.ready)
  await page.evaluate(() => document.fonts.ready)
  await page.waitForTimeout(500)
}

