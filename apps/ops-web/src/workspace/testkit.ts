// Helpers for the tests in this folder only: a fake fetch that records every request, and small
// builders for the JSON the API sends. Not used by the app.

import type { Fetcher } from './api.ts'
import type { ConfirmedState, RunningResponse, ScheduledJourney, Station } from './types.ts'

export type Call = { method: string; path: string; headers: Record<string, string>; body: unknown }

/** An answer a fake route can give. 'lost' makes fetch itself fail, as a dropped connection does. */
export type Answer = Response | 'lost'

export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}

export function problem(status: number, detail: string, extra: Record<string, unknown> = {}): Response {
  return json({ title: 'Problem', detail, status, ...extra }, status)
}

/**
 * A fetch that hands each request to `route`. `path` includes the query and has no origin, so
 * tests stay independent of the API address. Every request is recorded in `calls`.
 */
export function fakeFetch(route: (call: Call) => Answer | Promise<Answer>): { fetcher: Fetcher; calls: Call[] } {
  const calls: Call[] = []
  const fetcher: Fetcher = async (url, init = {}) => {
    const parsed = new URL(url, 'http://api.test')
    const headers = Object.fromEntries(new Headers(init.headers).entries())
    const call: Call = {
      method: init.method ?? 'GET',
      path: parsed.pathname + parsed.search,
      headers,
      body: typeof init.body === 'string' ? JSON.parse(init.body) : undefined,
    }
    calls.push(call)
    const answer = await route(call)
    if (answer === 'lost') throw new TypeError('fetch failed')
    return answer
  }
  return { fetcher, calls }
}

export const stations: Station[] = [
  { id: 'st-gph', code: 'GPH', nameEn: 'Gampaha', nameSi: '', nameTa: '' },
  { id: 'st-rgm', code: 'RGM', nameEn: 'Ragama', nameSi: '', nameTa: '' },
  { id: 'st-fot', code: 'FOT', nameEn: 'Colombo Fort', nameSi: '', nameTa: '' },
]

/** Three stops: Gampaha, Ragama, Colombo Fort. Times are Sri Lanka time (+05:30) on the given date. */
export function journey(id: string, trainNumber: string, serviceDate: string, depart = '07:35', arrive = '08:30'): ScheduledJourney {
  const at = (time: string) => `${serviceDate}T${time}:00+05:30`
  return {
    journeyId: id,
    serviceDate,
    trainNumber,
    sourceKind: 'FICTIONAL_DEMO',
    sourceLabel: 'Fictional demo',
    stops: [
      { stopId: `${id}-1`, sequence: 1, stationId: 'st-gph', stationCode: 'GPH', scheduledArrival: null, scheduledDeparture: at(depart), plannedPlatform: '2' },
      { stopId: `${id}-2`, sequence: 2, stationId: 'st-rgm', stationCode: 'RGM', scheduledArrival: at('07:50'), scheduledDeparture: at('07:51'), plannedPlatform: '1' },
      { stopId: `${id}-3`, sequence: 3, stationId: 'st-fot', stationCode: 'FOT', scheduledArrival: at(arrive), scheduledDeparture: null, plannedPlatform: '4' },
    ],
  }
}

export function running(j: ScheduledJourney, observedAt: string | null, overrides: Partial<RunningResponse> = {}): RunningResponse {
  return {
    journeyId: j.journeyId,
    serviceDate: j.serviceDate,
    trainNumber: j.trainNumber,
    timetable: { kind: j.sourceKind, label: j.sourceLabel, url: null, checkedAt: null },
    runningLabel: 'SmartRail simulation',
    thresholds: { ageingAfterSeconds: 180, unknownAfterSeconds: 240 },
    freshness: 'CURRENT',
    lastObservation: observedAt
      ? {
          observedAt,
          receivedAt: observedAt,
          fromStopId: j.stops[0].stopId,
          toStopId: j.stops[1].stopId,
          progressPermille: 1000,
          reportedDelaySeconds: 240,
          lastKnownStationId: 'st-rgm',
        }
      : null,
    currentPosition: null,
    delaySeconds: observedAt ? 240 : null,
    availability: 'AS_SCHEDULED',
    confirmedDelayMinutes: null,
    finalStopId: null,
    stops: j.stops.map((s) => ({
      stopId: s.stopId,
      stationId: s.stationId,
      sequence: s.sequence,
      scheduledArrival: s.scheduledArrival,
      scheduledDeparture: s.scheduledDeparture,
      plannedPlatform: s.plannedPlatform,
      estimatedArrival: null,
      estimatedDeparture: null,
      served: true,
      confirmedPlatform: null,
      confirmedPlatformAt: null,
    })),
    ...overrides,
  }
}

export function confirmedState(journeyId: string, version: number, overrides: Partial<ConfirmedState> = {}): ConfirmedState {
  return {
    journeyId,
    journeyVersion: version,
    availability: 'AS_SCHEDULED',
    confirmedDelayMinutes: null,
    finalStopId: null,
    orderClosed: false,
    activeChanges: [],
    stops: [],
    ...overrides,
  }
}
