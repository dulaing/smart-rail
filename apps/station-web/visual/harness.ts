// Shared by the visual and smoke checks: the approved Station states as test scenarios, and the
// routine that opens one with a mocked, deterministic API and a frozen Colombo clock.

import { readFileSync } from 'node:fs'
import type { Page } from '@playwright/test'
import { ageing, cancellation, changesOf, confirmedDelay, crowded, day, endsEarly, failedRead, finalArrival, fortArrivals, gampahaDepartures, journeysResponse, moveWithdrawn, notServed, originatingDepartures, overnight, platformCancelled, platformEndsEarly, platformEtaUnknown, platformMoved, platformMovedTo, platformNext, platformNotStopping, platformOnTime, reconnected, runningOf, savedTimetable, savedWithPlatform3, stations, unknownEta, type Train } from './scenarios.ts'


export const out = 'visual/comparisons/2026-10-20'
export const bounds = JSON.parse(readFileSync(`${out}/station.reference-bounds.json`, 'utf8')) as Record<string, Record<string, Record<string, number>>>

export type Step = {
  /** Colombo wall time the page clock moves to. */
  now: string
  /** What the API answers from now on. */
  trains?: Train[]
  offline?: boolean
  hang?: boolean
  /** Ask the board to refresh now (the way going online or becoming visible does). Default true. */
  refresh?: boolean
  /** A selector to wait for once the step has run. */
  ready?: string
}

export type Scenario = {
  url: string
  /** Colombo wall time on the fixture date. */
  now: string
  trains: Train[]
  /** The saved snapshot the board starts from (offline), downloaded at this Colombo time. */
  saved?: string
  /** The service date of the saved snapshot; the fixture date unless stated. */
  savedDate?: string
  /** Every API read fails. */
  offline?: boolean
  /** No API read ever answers (the first load). */
  hang?: boolean
  ready: string
  /** Later moments of the same page: the clock moves on, the API changes, the board refreshes. */
  steps?: Step[]
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
  // The read for DS-C1 worked once (its platform was confirmed) and fails on the next refresh.
  'entrance-failed-read': { url: entrance, now: '07:08', trains: unknownEta, steps: [{ now: '07:08', trains: failedRead }], ready: '.row', reference: 'states/238-1297-failed-read.png' },
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
  // The board refreshed at 22:29; the clock has moved on to 22:30.
  'platform-no-next': { url: platform(3), now: '22:29', trains: [], steps: [{ now: '22:30', refresh: false }], ready: '.service-empty', reference: 'states/236-1093-platform-no-next.png' },
  'entrance-originating': { url: '/?station=FOT&view=entrance', now: '07:08', trains: originatingDepartures, ready: '.row', reference: 'states/231-305-originating-departures.png' },
  'entrance-final-arrival': { url: '/?station=RGM&view=entrance', now: '07:38', trains: finalArrival, ready: '.row', reference: 'states/234-465-final-arrival.png' },
  'platform-final-arrival': { url: '/?station=RGM&view=platform&platform=2', now: '07:38', trains: finalArrival, ready: '.service-card', reference: 'states/236-907-platform-final-arrival.png' },
  'platform-moved-to': { url: platform(3), now: '07:20', trains: platformMovedTo, ready: '.service-card', reference: 'states/236-659-platform-moved-to.png' },
  // Staff withdrew the move to platform 3; the board notices at its 07:21 refresh, the clock is 07:22.
  'platform-move-withdrawn': { url: platform(2), now: '07:20', trains: platformMoved, steps: [{ now: '07:21', trains: moveWithdrawn }, { now: '07:22', refresh: false }], ready: '.service-card', reference: 'states/236-721-move-withdrawn.png' },
  'platform-on-time': { url: platform(2), now: '07:29', trains: platformOnTime, ready: '.service-card', reference: 'states/236-1031-departed-move-cleared.png' },
  'platform-eta-unknown': { url: platform(3), now: '07:20', trains: platformEtaUnknown, ready: '.service-card', reference: 'states/238-1424-eta-unknown-platform-confirmed.png' },
  'platform-no-matching-saved': { url: platform(3), now: '07:20', trains: savedWithPlatform3, saved: '07:05', offline: true, ready: '.service-empty', reference: 'states/239-1489-no-matching-saved.png' },
  'entrance-refreshing': { url: entrance, now: '07:21', trains: savedTimetable, saved: '07:05', hang: true, ready: '.row', reference: 'states/239-1618-reconnect-refresh.png' },
  // Offline at 07:21, back at 07:22: the first live answer after the loss.
  'entrance-reconnected': { url: entrance, now: '07:21', trains: reconnected, saved: '07:05', offline: true, steps: [{ now: '07:22', offline: false }], ready: '.row', reference: 'states/239-1724-reconnected.png' },
  'entrance-expired-date': { url: entrance, now: '00:08', trains: overnight, saved: '23:30', savedDate: '2026-10-11', offline: true, ready: '.row', reference: 'states/239-1851-expired-service-date.png' },
}

/**
 * Opens a scenario; `base` is the origin to load it from (the dev server unless told otherwise).
 * The returned state is what the mocked API answers from; a check may change it while the page runs.
 */
export async function open(page: Page, scenario: Scenario, base?: string) {
  const state = { now: scenario.now, trains: scenario.trains, offline: scenario.offline ?? false, hang: scenario.hang ?? false }
  const instant = (time: string) => new Date(`${day}T${time}:00+05:30`)
  await page.clock.setFixedTime(instant(state.now))
  if (scenario.saved) {
    const savedDay = scenario.savedDate ?? day
    const snapshot = {
      version: 1,
      stations,
      stationsSavedAt: new Date(`${day}T${scenario.saved}:00+05:30`).getTime(),
      days: { [savedDay]: { serviceDate: savedDay, downloadedAt: new Date(`${savedDay}T${scenario.saved}:00+05:30`).getTime(), journeys: journeysResponse(scenario.trains, savedDay).journeys } },
    }
    await page.addInitScript((value) => window.localStorage.setItem('smartrail.station-board', value), JSON.stringify(snapshot))
  }
  await page.route('https://localhost:7074/api/**', async (route) => {
    if (state.hang) return new Promise<void>(() => {})
    if (state.offline) return route.abort('internetdisconnected')
    const url = new URL(route.request().url())
    const headers = { 'content-type': 'application/json', 'x-smartrail-server-time': instant(state.now).toISOString(), 'access-control-allow-origin': '*', 'access-control-expose-headers': 'X-SmartRail-Server-Time' }
    const json = (body: unknown) => route.fulfill({ status: 200, headers, body: JSON.stringify(body) })
    if (url.pathname === '/api/stations') return json(stations)
    if (url.pathname === '/api/journeys') return json(journeysResponse(state.trains, url.searchParams.get('serviceDate') ?? day))
    const match = /^\/api\/journeys\/([^/]+)\/(running|changes)$/.exec(url.pathname)
    const train = state.trains.find((t) => t.id === match?.[1])
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
  for (const step of scenario.steps ?? []) {
    state.now = step.now
    if (step.trains) state.trains = step.trains
    if (step.offline !== undefined) state.offline = step.offline
    if (step.hang !== undefined) state.hang = step.hang
    await page.clock.setFixedTime(instant(step.now))
    if (step.refresh !== false) await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')))
    if (step.ready) await page.waitForSelector(step.ready)
    await page.waitForTimeout(1300)
  }
  return state
}
