// Small hand-built data for the tests (run with `npm test`). It mirrors the shapes of the public
// reads and the fictional SIM-1001 train in the demo seed; none of it is shown by the app.

import type { ChangesResponse, Journey, RunningResponse, Station } from './api.ts'

export const date = '2026-10-15'

/** An instant on the fixture date, written as Colombo wall time such as "07:35". */
export const at = (hhmm: string, day = date): number => Date.parse(`${day}T${hhmm}:00+05:30`)
/** "00:25+1" is 00:25 on the day after the service date, like a stop with day offset 1. */
const iso = (hhmm: string | null, day = date) => {
  if (hhmm === null) return null
  const [time, plus] = hhmm.split('+')
  const shifted = new Date(`${day}T00:00:00Z`)
  shifted.setUTCDate(shifted.getUTCDate() + Number(plus ?? 0))
  return new Date(at(time, shifted.toISOString().slice(0, 10))).toISOString()
}

export const stations: Station[] = [
  { id: 's-gph', code: 'GPH', nameEn: 'Gampaha', nameSi: 'ගම්පහ', nameTa: 'கம்பஹா' },
  { id: 's-rgm', code: 'RGM', nameEn: 'Ragama', nameSi: 'රාගම', nameTa: 'ராகமை' },
  { id: 's-mda', code: 'MDA', nameEn: 'Maradana', nameSi: 'මරදාන', nameTa: 'மருதானை' },
  { id: 's-fot', code: 'FOT', nameEn: 'Colombo Fort', nameSi: 'කොළඹ කොටුව', nameTa: 'கொழும்பு கோட்டை' },
]

type StopSpec = [stop: string, station: string, arrival: string | null, departure: string | null, platform: string]

/** SIM-1001: Gampaha 07:35 (platform 2) to Colombo Fort 08:30 (platform 4). */
export const specs: StopSpec[] = [
  ['st-1', 's-gph', null, '07:35', '2'],
  ['st-2', 's-rgm', '07:50', '07:51', '1'],
  ['st-3', 's-mda', '08:20', '08:22', '3'],
  ['st-4', 's-fot', '08:30', null, '4'],
]

export function journey(trainNumber = 'SIM-1001', id = 'j-1001', serviceDate = date, stopSpecs = specs): Journey {
  return {
    journeyId: id,
    serviceDate,
    trainNumber,
    sourceKind: 'FICTIONAL_DEMO',
    sourceLabel: 'Fictional demo timetable',
    sourceUrl: null,
    sourceCheckedAt: null,
    stops: stopSpecs.map(([stopId, stationId, arrival, departure, platform], index) => ({
      stopId,
      sequence: index + 1,
      stationId,
      stationCode: stationId.slice(2).toUpperCase(),
      scheduledArrival: iso(arrival, serviceDate),
      scheduledDeparture: iso(departure, serviceDate),
      plannedPlatform: platform,
    })),
  }
}

type RunningOptions = {
  id?: string
  freshness?: RunningResponse['freshness']
  /** The newest observation: the train is `permille` of the way from one stop to the next. */
  observation?: { from: string; to: string; permille: number; observedAt: string } | null
  availability?: RunningResponse['availability']
  finalStopId?: string | null
  confirmedDelayMinutes?: number | null
  /** Estimated departure (and arrival) by stop ID, as the server would send while evidence is usable. */
  estimates?: Record<string, [arrival: string | null, departure: string | null]>
  /** Confirmed platform by stop ID with its confirmation time. */
  platforms?: Record<string, [platform: string, confirmedAt: string]>
  /** Which stops are served; defaults to all. */
  unserved?: string[]
  stopSpecs?: StopSpec[]
}

export function running(options: RunningOptions = {}): RunningResponse {
  const {
    id = 'j-1001',
    freshness = 'CURRENT',
    observation = null,
    availability = 'AS_SCHEDULED',
    finalStopId = null,
    confirmedDelayMinutes = null,
    estimates = {},
    platforms = {},
    unserved = [],
    stopSpecs = specs,
  } = options
  return {
    journeyId: id,
    thresholds: { ageingAfterSeconds: 180, unknownAfterSeconds: 240 },
    freshness,
    lastObservation: observation
      ? { observedAt: observation.observedAt, fromStopId: observation.from, toStopId: observation.to, progressPermille: observation.permille }
      : null,
    availability,
    confirmedDelayMinutes,
    finalStopId,
    stops: stopSpecs.map(([stopId, stationId, arrival, departure, platform], index) => {
      const confirmed = platforms[stopId]
      return {
        stopId,
        stationId,
        sequence: index + 1,
        scheduledArrival: iso(arrival),
        scheduledDeparture: iso(departure),
        plannedPlatform: platform,
        estimatedArrival: iso(estimates[stopId]?.[0] ?? null),
        estimatedDeparture: iso(estimates[stopId]?.[1] ?? null),
        served: !unserved.includes(stopId),
        confirmedPlatform: confirmed?.[0] ?? null,
        confirmedPlatformAt: confirmed?.[1] ?? null,
        platform: confirmed?.[0] ?? platform,
      }
    }),
  }
}

export function changes(active: ChangesResponse['activeChanges'] = [], id = 'j-1001'): ChangesResponse {
  return { journeyId: id, journeyVersion: 1, activeChanges: active }
}
