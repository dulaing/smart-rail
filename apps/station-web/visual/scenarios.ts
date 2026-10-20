// Test-only fixtures for the visual checks. They reproduce the trains, times and statuses written in
// the approved Figma frames (Monday 12 October 2026, Gampaha and Colombo Fort) so a capture can be
// laid over its reference. Nothing here is shown by the app or reachable from a production build.

import type { ChangesResponse, Journey, JourneysResponse, RunningResponse, Station } from '../src/api.ts'

export const day = '2026-10-12'
export const at = (hhmm: string, date = day): string => new Date(`${date}T${hhmm}:00+05:30`).toISOString()

export const stations: Station[] = [
  { id: 's-vya', code: 'VYA', nameEn: 'Veyangoda', nameSi: 'වේයන්ගොඩ', nameTa: 'வெயங்கொடை' },
  { id: 's-gph', code: 'GPH', nameEn: 'Gampaha', nameSi: 'ගම්පහ', nameTa: 'கம்பஹா' },
  { id: 's-rgm', code: 'RGM', nameEn: 'Ragama', nameSi: 'රාගම', nameTa: 'ராகம' },
  { id: 's-fot', code: 'FOT', nameEn: 'Colombo Fort', nameSi: 'කොළඹ කොටුව', nameTa: 'கொழும்பு கோட்டை' },
]

/** One stop: [station, arrival, departure, planned platform]. */
export type Stop = [station: string, arrival: string | null, departure: string | null, platform: string]

export type Train = {
  id: string
  number: string
  stops: Stop[]
  /** Live evidence; omit for a train with none. */
  live?: {
    observedAt: string
    /** Index of the stop the train is heading to (it is on the segment before it). */
    toIndex: number
    freshness?: RunningResponse['freshness']
    availability?: RunningResponse['availability']
    finalIndex?: number
    /** Estimated [arrival, departure] by stop index. */
    estimates?: Record<number, [string | null, string | null]>
    /** Confirmed platform by stop index: [platform, confirmedAt]. */
    platforms?: Record<number, [string, string]>
    unserved?: number[]
    confirmedDelayMinutes?: number
  }
  /** Staff-confirmed changes for the changes read. */
  changes?: ChangesResponse['activeChanges']
  /** The running read fails for this train. */
  readFails?: boolean
}

const stopId = (train: Train, index: number) => `${train.id}-s${index + 1}`
const stationIdOf = (code: string) => stations.find((s) => s.code === code)!.id

export function journeyOf(train: Train): Journey {
  return {
    journeyId: train.id,
    serviceDate: day,
    trainNumber: train.number,
    sourceKind: 'FICTIONAL_DEMO',
    sourceLabel: 'Fictional demo timetable',
    sourceUrl: null,
    sourceCheckedAt: null,
    stops: train.stops.map(([code, arrival, departure, platform], index) => ({
      stopId: stopId(train, index),
      sequence: index + 1,
      stationId: stationIdOf(code),
      stationCode: code,
      scheduledArrival: arrival ? at(arrival) : null,
      scheduledDeparture: departure ? at(departure) : null,
      plannedPlatform: platform,
    })),
  }
}

export function runningOf(train: Train): RunningResponse | null {
  const live = train.live
  if (!live) return null
  const final = live.finalIndex === undefined ? null : stopId(train, live.finalIndex)
  return {
    journeyId: train.id,
    thresholds: { ageingAfterSeconds: 180, unknownAfterSeconds: 240 },
    freshness: live.freshness ?? 'CURRENT',
    lastObservation: {
      observedAt: at(live.observedAt),
      fromStopId: stopId(train, live.toIndex - 1),
      toStopId: stopId(train, live.toIndex),
      progressPermille: 800,
    },
    availability: live.availability ?? 'AS_SCHEDULED',
    confirmedDelayMinutes: live.confirmedDelayMinutes ?? null,
    finalStopId: final,
    stops: train.stops.map(([code, arrival, departure, platform], index) => {
      const estimate = live.estimates?.[index]
      const confirmed = live.platforms?.[index]
      return {
        stopId: stopId(train, index),
        stationId: stationIdOf(code),
        sequence: index + 1,
        scheduledArrival: arrival ? at(arrival) : null,
        scheduledDeparture: departure ? at(departure) : null,
        plannedPlatform: platform,
        estimatedArrival: estimate?.[0] ? at(estimate[0]) : null,
        estimatedDeparture: estimate?.[1] ? at(estimate[1]) : null,
        served: !(live.unserved ?? []).includes(index),
        confirmedPlatform: confirmed ? confirmed[0] : null,
        confirmedPlatformAt: confirmed ? at(confirmed[1]) : null,
        platform: confirmed ? confirmed[0] : platform,
      }
    }),
  }
}

export function changesOf(train: Train): ChangesResponse {
  return { journeyId: train.id, journeyVersion: 1, activeChanges: train.changes ?? [] }
}

export function journeysResponse(trains: Train[], date: string): JourneysResponse {
  return { serviceDate: date, scheduleNote: '', journeys: date === day ? trains.map(journeyOf) : [] }
}

/** Three trains from Gampaha to Colombo Fort, as in the entrance frames. */
export const gampahaDepartures: Train[] = [
  {
    id: 'j-a1',
    number: 'DS-A1',
    stops: [['VYA', null, '07:00', '1'], ['GPH', '07:23', '07:24', '2'], ['FOT', '08:05', null, '4']],
    live: { observedAt: '07:07', toIndex: 1, estimates: { 1: ['07:23', '07:24'] } },
  },
  {
    id: 'j-b1',
    number: 'DS-B1',
    stops: [['VYA', null, '06:45', '1'], ['GPH', '07:09', '07:10', '2'], ['FOT', '07:55', null, '4']],
    live: { observedAt: '07:07', toIndex: 1, estimates: { 1: ['07:26', '07:27'] }, platforms: { 1: ['3', '07:04'] } },
  },
  {
    id: 'j-c1',
    number: 'DS-C1',
    stops: [['VYA', null, '07:15', '1'], ['GPH', '07:39', '07:40', '1'], ['FOT', '08:20', null, '4']],
    live: { observedAt: '07:07', toIndex: 1, estimates: { 1: ['07:39', '07:40'] } },
  },
]

/** The same stop on a platform view, 07:20: the next train is DS-A1 on platform 2. */
export const platformNext: Train[] = [
  {
    id: 'j-a1',
    number: 'DS-A1',
    stops: [['VYA', null, '07:00', '1'], ['GPH', '07:23', '07:24', '2'], ['FOT', '08:05', null, '4']],
    live: { observedAt: '07:19', toIndex: 1, estimates: { 1: ['07:23', '07:24'] } },
  },
]

/** The saved timetable of the offline frame: two trains to Colombo Fort, no live evidence. */
export const savedTimetable: Train[] = [
  { id: 'j-b1', number: 'DS-B1', stops: [['GPH', null, '07:10', '2'], ['FOT', '07:55', null, '4']] },
  { id: 'j-a1', number: 'DS-A1', stops: [['GPH', null, '07:24', '2'], ['FOT', '08:05', null, '4']] },
]

const gph = (id: string, number: string, scheduled: string, platform: string, live?: Train['live'], extra: Partial<Train> = {}): Train => ({
  id,
  number,
  stops: [['VYA', null, '06:45', '1'], ['GPH', scheduled, scheduled, platform], ['FOT', '08:30', null, '4']],
  live,
  ...extra,
})

const delayedB1 = (observedAt = '07:07', platforms: Record<number, [string, string]> = { 1: ['3', '07:04'] }, planned = '2') =>
  gph('j-b1', 'DS-B1', '07:10', planned, { observedAt, toIndex: 1, estimates: { 1: ['07:27', '07:27'] }, platforms })

/** Arrivals at Colombo Fort at 08:08: all three trains end their journey here. */
export const fortArrivals: Train[] = [
  { id: 'j-a1', number: 'DS-A1', stops: [['RGM', null, '07:50', '1'], ['FOT', '08:12', null, '2']], live: { observedAt: '08:07', toIndex: 1, estimates: { 1: ['08:12', null] } } },
  { id: 'j-b1', number: 'DS-B1', stops: [['VYA', null, '07:30', '1'], ['FOT', '08:01', null, '4']], live: { observedAt: '08:07', toIndex: 1, estimates: { 1: ['08:18', null] } } },
  { id: 'j-c1', number: 'DS-C1', stops: [['GPH', null, '08:00', '1'], ['FOT', '08:30', null, '1']], live: { observedAt: '08:07', toIndex: 1, estimates: { 1: ['08:30', null] } } },
]

const cancelledD1 = (): Train =>
  gph('j-d1', 'DS-D1', '08:05', '2', { observedAt: '07:07', toIndex: 1, availability: 'CANCELLED' }, {
    changes: [{ changeId: 'c-1', kind: 'CANCEL', confirmedAt: at('07:02'), delayMinutes: null, newPlatform: null, targetStopId: null, finalStopId: null }],
  })

/** A cancellation above a normal and a delayed train. */
export const cancellation: Train[] = [cancelledD1(), gph('j-a1', 'DS-A1', '07:24', '2', { observedAt: '07:07', toIndex: 1, estimates: { 1: ['07:24', '07:24'] } }), delayedB1()]

/** The observation frames plan DS-B1 on platform 3, so nothing has moved. */
const withC1 = (live: Train['live'], extra: Partial<Train> = {}) => [
  gph('j-a1', 'DS-A1', '07:24', '2', { observedAt: '07:07', toIndex: 1, estimates: { 1: ['07:24', '07:24'] } }),
  delayedB1('07:07', {}, '3'),
  gph('j-c1', 'DS-C1', '07:40', '1', live, extra),
]

/** DS-C1 was last seen five minutes ago: its time is unknown, its confirmed platform is not. */
export const unknownEta: Train[] = withC1({ observedAt: '07:03', toIndex: 1, platforms: { 1: ['1', '06:59'] } })
/** DS-C1 was last seen three minutes ago: ageing, with its estimate still shown. */
export const ageing: Train[] = withC1({ observedAt: '07:05', toIndex: 1, estimates: { 1: ['07:40', '07:40'] } })
/** The running read for DS-C1 fails; the others are fine. */
export const failedRead: Train[] = withC1(undefined, { readFails: true })

/** Platform 2 at 07:20: DS-B1 was moved to platform 3 at 07:04. */
export const platformMoved: Train[] = [delayedB1('07:19')]

/** Platform 2 at 07:20: DS-D1 is cancelled. */
export const platformCancelled: Train[] = [cancelledD1()]

const earlyEnd = (confirmedAt: string): Train['changes'] => [{ changeId: 'c-2', kind: 'EARLY_END', confirmedAt: at(confirmedAt), delayMinutes: null, newPlatform: null, targetStopId: null, finalStopId: null }]

/** DS-B1 now ends at Ragama, so Gampaha still sees it leave, for Ragama only. */
const b1EndsAtRagama = (observedAt: string, platforms: Record<number, [string, string]>, planned = '2'): Train => ({
  id: 'j-b1',
  number: 'DS-B1',
  stops: [['VYA', null, '06:45', '1'], ['GPH', '07:09', '07:10', planned], ['RGM', '07:30', '07:31', '1'], ['FOT', '07:55', null, '4']],
  live: { observedAt, toIndex: 1, availability: 'ENDS_EARLY', finalIndex: 2, estimates: { 1: ['07:27', '07:27'] }, platforms },
  changes: earlyEnd('07:06'),
})

/** At Gampaha: DS-B1 now ends at Ragama. */
export const endsEarly: Train[] = [
  gph('j-a1', 'DS-A1', '07:24', '2', { observedAt: '07:07', toIndex: 1, estimates: { 1: ['07:24', '07:24'] } }),
  b1EndsAtRagama('07:07', { 1: ['3', '07:04'] }),
]

/** Platform 3 at Gampaha at 07:20: DS-B1 now ends at Ragama. */
export const platformEndsEarly: Train[] = [b1EndsAtRagama('07:19', {}, '3')]

/** At Colombo Fort at 08:08: DS-B1 now ends at Ragama and no longer reaches here. */
export const notServed: Train[] = [
  { id: 'j-a1', number: 'DS-A1', stops: [['RGM', null, '07:50', '1'], ['FOT', '08:12', null, '2']], live: { observedAt: '08:07', toIndex: 1, estimates: { 1: ['08:12', null] } } },
  {
    id: 'j-b1',
    number: 'DS-B1',
    stops: [['VYA', null, '07:30', '1'], ['RGM', '07:50', '07:51', '1'], ['FOT', '08:01', null, '4']],
    live: { observedAt: '08:07', toIndex: 1, availability: 'ENDS_EARLY', finalIndex: 1, unserved: [2] },
    changes: earlyEnd('07:06'),
  },
]

/** Platform 4 at Colombo Fort at 08:08: the same train, which does not stop here. */
export const platformNotStopping: Train[] = [notServed[1]]

/** A staff-confirmed 17 minute delay for DS-B1, shown in the waiting area. */
export const confirmedDelay: Train[] = [
  gph('j-a1', 'DS-A1', '07:24', '2', { observedAt: '07:07', toIndex: 1, estimates: { 1: ['07:24', '07:24'] } }),
  gph('j-b1', 'DS-B1', '07:10', '2', { observedAt: '07:07', toIndex: 1, estimates: { 1: ['07:27', '07:27'] }, platforms: { 1: ['3', '07:04'] }, confirmedDelayMinutes: 17 }, {
    changes: [{ changeId: 'c-3', kind: 'DELAY', confirmedAt: at('07:04'), delayMinutes: 17, newPlatform: null, targetStopId: null, finalStopId: null }],
  }),
]

/** Nine services at once, with a cancellation: more than the board can show whole. */
export const crowded: Train[] = [
  cancelledD1(),
  ...['07:12', '07:24', '07:36', '07:48', '08:00', '08:12', '08:24', '08:36'].map((time, i) =>
    gph(`j-x${i}`, `DS-X${i + 1}`, time, String((i % 3) + 1), { observedAt: '07:07', toIndex: 1, estimates: { 1: [time, time] } }),
  ),
]
