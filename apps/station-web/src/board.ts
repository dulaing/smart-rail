// Turns the public timetable plus the running and change reads into the rows a board shows. It is
// pure: no fetching, no clock, no React. The server already worked out the estimates, platforms and
// availability; this only chooses which rows to show and how to read them honestly. It never works
// out an ETA itself.

import type { Evidence } from '../../../shared/design/generated/status.ts'
import { freshnessOf } from '../../../shared/design/freshness.ts'
import type { ChangesResponse, Journey, JourneyStop, RunningResponse, RunningStop } from './api.ts'

/** Trains are looked for from this long before now (a late train must not vanish) to this long ahead. */
export const lookbackMs = 3 * 60 * 60 * 1000
export const aheadMs = 6 * 60 * 60 * 1000
/** A train nobody has ever observed, and a cancellation, stay this long past their scheduled time. */
export const graceMs = 15 * 60 * 1000
/** Upper bound; the entrance also measures the space needed by complete trilingual rows. */
export const entranceRowLimit = 6

/** Fit complete rows without shrinking the station type. Confirmed disruptions take priority
 * over ordinary services; the selected rows keep their upcoming-time order. */
export function fitEntranceRows(rows: readonly BoardRow[], heights: readonly number[], available: number): Set<string> {
  const selected = new Set<string>()
  let remaining = available
  const changed = (row: BoardRow) => row.cancelled || row.unservedHere || row.endsEarly || row.moved !== null || row.confirmedDelay !== null
  const indices = rows.map((_, i) => i)
  for (const i of [...indices.filter(i => changed(rows[i])), ...indices.filter(i => !changed(rows[i]))]) {
    if (selected.size >= entranceRowLimit) break
    if (heights[i] <= remaining) {
      selected.add(rows[i].key)
      remaining -= heights[i]
    }
  }
  return selected
}
/** A cancelled train, or one that no longer stops here, is a notice above the list, not a row. */
export const isNotice = (row: BoardRow): boolean => row.cancelled || row.unservedHere

/** Rows whose change also gets a notice card: those above, a train ending early, a staff-confirmed delay. */
export const hasNotice = (row: BoardRow): boolean => isNotice(row) || row.endsEarly || row.confirmedDelay !== null

/** An expected time this much later than the scheduled one is a delay worth saying. */
const delayThresholdMs = 60 * 1000

/** What the board knows about one journey from the live reads. */
export type JourneyLive = {
  running: RunningResponse | null
  changes: ChangesResponse | null
  /** The last attempt to refresh this train failed; running/changes hold whatever succeeded before. */
  refreshFailed: boolean
  /** Device-corrected time of the last successful running read, for the "last checked" line. */
  checkedAt: number | null
}

export type BoardInput = {
  stationId: string
  journeys: readonly Journey[]
  live: ReadonlyMap<string, JourneyLive>
  /** Corrected with the server clock. */
  now: number
  /** Whole-feed loss, or nothing fetched yet: every row is timetable only. */
  timetableOnly: boolean
}

export type BoardRow = {
  /** Journey and stop occurrence, so the same stop never produces two rows or two notices. */
  key: string
  journeyId: string
  /** The service date the journey belongs to; an overnight train after midnight belongs to the day before. */
  serviceDate: string
  stopId: string
  trainNumber: string
  /** The row is about this stop's arrival (the train ends here) or its departure. */
  kind: 'departure' | 'arrival'
  startsHere: boolean
  endsHere: boolean
  scheduledAt: number
  /** Null whenever it cannot be supported: unknown evidence, cancellation, unserved stop, timetable only. */
  expectedAt: number | null
  /** Where the train goes (departure) or came from (arrival). */
  otherStationId: string
  /** The platform to show: confirmed when live, else the planned one. */
  platform: string | null
  plannedPlatform: string | null
  moved: { from: string; to: string; confirmedAt: number | null } | null
  cancelled: boolean
  /** The train ends before this stop, which it no longer serves. */
  unservedHere: boolean
  endsEarly: boolean
  /** The new final station of an early ending. */
  endsAtStationId: string | null
  delayed: boolean
  delayMinutes: number | null
  /** Only ever true with CURRENT evidence. */
  onTime: boolean
  evidence: Evidence
  /** When the newest position observation for this train was made; null when none, or timetable only. */
  observedAt: number | null
  /** When the platform shown was staff-confirmed, if it was. */
  platformConfirmedAt: number | null
  /** Staff-confirmed delay with its own confirmation time, shown even when position is unknown. */
  confirmedDelay: { minutes: number; at: number | null } | null
  /** When the cancellation or early ending was confirmed. */
  confirmedChangeAt: number | null
  refreshFailed: boolean
  checkedAt: number | null
  timetableOnly: boolean
  /** The time to order by: expected when supported, else scheduled. */
  sortAt: number
}

const ms = (iso: string | null | undefined): number | null => {
  if (!iso) return null
  const value = Date.parse(iso)
  return Number.isNaN(value) ? null : value
}

/** Where the last observation puts the train, in stop order, or null when it cannot be placed. */
function progressOf(running: RunningResponse, stops: readonly RunningStop[]): { toIndex: number; atStop: boolean } | null {
  const o = running.lastObservation
  if (!o) return null
  const toIndex = stops.findIndex((s) => s.stopId === o.toStopId)
  if (toIndex < 1 || stops[toIndex - 1].stopId !== o.fromStopId) return null
  if (o.progressPermille < 0 || o.progressPermille > 1000) return null
  return { toIndex, atStop: o.progressPermille === 1000 }
}

/** The evidence class now: the age of the observation, never the age of the last HTTP response. */
export function evidenceOf(running: RunningResponse | null, now: number): Evidence {
  if (!running) return 'UNKNOWN'
  // The server says UNKNOWN when it cannot place the evidence, and then it also sends no estimates.
  if (running.freshness === 'UNKNOWN') return 'UNKNOWN'
  return freshnessOf(running.lastObservation?.observedAt ?? null, now, running.thresholds)
}

/** One row for each stop of each journey that is relevant to this station right now. */
export function deriveRows(input: BoardInput): BoardRow[] {
  const rows: BoardRow[] = []
  for (const journey of input.journeys) {
    const stops = [...journey.stops].sort((a, b) => a.sequence - b.sequence)
    stops.forEach((stop, index) => {
      if (stop.stationId !== input.stationId) return
      const row = rowFor(journey, stops, index, input)
      if (row && isRelevant(row, input)) rows.push(row)
    })
  }
  return rows.sort((a, b) => a.sortAt - b.sortAt || a.trainNumber.localeCompare(b.trainNumber))
}

function rowFor(journey: Journey, stops: readonly JourneyStop[], index: number, input: BoardInput): BoardRow | null {
  const stop = stops[index]
  const live = input.timetableOnly ? undefined : input.live.get(journey.journeyId)
  const running = live?.running ?? null
  const runningStops = running ? [...running.stops].sort((a, b) => a.sequence - b.sequence) : []
  const runningStop = runningStops.find((s) => s.stopId === stop.stopId)

  const availability = running?.availability ?? 'AS_SCHEDULED'
  const cancelled = availability === 'CANCELLED'
  const finalIndex =
    availability === 'ENDS_EARLY' ? stops.findIndex((s) => s.stopId === running?.finalStopId) : -1
  const endsEarly = finalIndex >= 0
  const effectiveLastIndex = endsEarly ? finalIndex : stops.length - 1
  const unservedHere = !cancelled && endsEarly && index > finalIndex

  const startsHere = index === 0
  const endsHere = !cancelled && !unservedHere && index === effectiveLastIndex && index > 0

  // A train that ends here is shown by its arrival; every other stop by its departure.
  const arrivalAt = ms(stop.scheduledArrival)
  const departureAt = ms(stop.scheduledDeparture)
  const kind: BoardRow['kind'] = endsHere || departureAt === null ? 'arrival' : 'departure'
  const scheduledAt = kind === 'arrival' ? arrivalAt : departureAt
  if (scheduledAt === null) return null

  const evidence = input.timetableOnly ? 'UNKNOWN' : evidenceOf(running, input.now)
  const usable = evidence !== 'UNKNOWN' && !cancelled && !unservedHere && runningStop?.served !== false
  const estimate = usable ? ms(kind === 'arrival' ? runningStop?.estimatedArrival : runningStop?.estimatedDeparture) : null

  // Whether the train has provably gone. This comes from where the newest observation placed it,
  // so a stop stays on the board while it is merely overdue.
  const progress = running ? progressOf(running, runningStops) : null
  const rowIndex = runningStops.findIndex((s) => s.stopId === stop.stopId)
  const passed =
    progress !== null &&
    rowIndex >= 0 &&
    (kind === 'departure' ? rowIndex < progress.toIndex : rowIndex < progress.toIndex || (progress.atStop && rowIndex === progress.toIndex))
  if (passed && !cancelled && !unservedHere) return null

  const confirmedPlatform = runningStop?.confirmedPlatform ?? null
  const planned = stop.plannedPlatform
  const moved =
    confirmedPlatform !== null && planned !== null && confirmedPlatform !== planned
      ? { from: planned, to: confirmedPlatform, confirmedAt: ms(runningStop?.confirmedPlatformAt) }
      : null

  const confirmedMinutes = running?.confirmedDelayMinutes ?? null
  const delayFromEstimate = estimate !== null && estimate - scheduledAt >= delayThresholdMs ? Math.round((estimate - scheduledAt) / 60_000) : null
  const delayMinutes = delayFromEstimate ?? (confirmedMinutes !== null && confirmedMinutes > 0 ? confirmedMinutes : null)
  const delayed = delayMinutes !== null && !cancelled && !unservedHere

  const changeTime = (kinds: string[]) =>
    live?.changes?.activeChanges
      .filter((c) => kinds.includes(c.kind))
      .map((c) => ms(c.confirmedAt))
      .reduce<number | null>((latest, t) => (t !== null && (latest === null || t > latest) ? t : latest), null) ?? null
  const delayChangeAt = changeTime(['DELAY'])

  const destination = stops[cancelled ? stops.length - 1 : effectiveLastIndex]
  const otherStop = kind === 'arrival' ? stops[0] : destination

  return {
    key: `${journey.journeyId}:${stop.stopId}`,
    journeyId: journey.journeyId,
    serviceDate: journey.serviceDate,
    stopId: stop.stopId,
    trainNumber: journey.trainNumber,
    kind,
    startsHere,
    endsHere,
    scheduledAt,
    expectedAt: estimate,
    otherStationId: otherStop.stationId,
    platform: input.timetableOnly ? planned : (runningStop?.platform ?? confirmedPlatform ?? planned),
    plannedPlatform: planned,
    moved: input.timetableOnly ? null : moved,
    cancelled,
    unservedHere,
    endsEarly: endsEarly && !unservedHere,
    endsAtStationId: endsEarly ? stops[finalIndex].stationId : null,
    delayed,
    delayMinutes: delayed ? delayMinutes : null,
    onTime: usable && evidence === 'CURRENT' && estimate !== null && !delayed,
    evidence,
    observedAt: input.timetableOnly ? null : ms(running?.lastObservation?.observedAt),
    platformConfirmedAt: input.timetableOnly ? null : ms(runningStop?.confirmedPlatformAt),
    confirmedDelay: delayed && confirmedMinutes !== null && confirmedMinutes > 0 ? { minutes: confirmedMinutes, at: delayChangeAt } : null,
    confirmedChangeAt: changeTime(['CANCEL', 'EARLY_END']),
    refreshFailed: live?.refreshFailed ?? false,
    checkedAt: live?.checkedAt ?? null,
    timetableOnly: input.timetableOnly,
    sortAt: estimate ?? scheduledAt,
  }
}

/**
 * Which rows stay on the board. A late train stays until evidence proves it has gone, however far
 * past its scheduled time. Without usable evidence it can only stay if something suggests it is
 * running: an observation was once made, or its time has barely passed.
 */
function isRelevant(row: BoardRow, input: BoardInput): boolean {
  const { now } = input
  if (row.scheduledAt > now + aheadMs) return false
  if (row.scheduledAt < now - lookbackMs) return false
  // A staff-confirmed delay is a decision, so it moves the time a train is still due (it is not shown as an ETA).
  const dueAt = row.scheduledAt + (row.confirmedDelay?.minutes ?? 0) * 60_000
  const withinGrace = now <= dueAt + graceMs

  if (row.timetableOnly || row.cancelled || row.unservedHere) return withinGrace
  if (row.evidence !== 'UNKNOWN') return true
  if (withinGrace) return true
  // Position unknown and overdue: keep only a train that is known to have been running.
  return input.live.get(row.journeyId)?.running?.lastObservation != null
}

/** Journeys whose stop here falls in the window where a live read could change what the board shows. */
export function liveCandidates(journeys: readonly Journey[], stationId: string, now: number): Journey[] {
  return journeys.filter((journey) =>
    journey.stops.some((stop) => {
      if (stop.stationId !== stationId) return false
      const at = ms(stop.scheduledDeparture) ?? ms(stop.scheduledArrival)
      return at !== null && at >= now - lookbackMs && at <= now + aheadMs
    }),
  )
}

/** The platform view: its next train, and the notices that belong to it. */
export type PlatformView = {
  next: BoardRow | null
  notices: BoardRow[]
}

export function platformView(rows: readonly BoardRow[], platform: string): PlatformView {
  // A train that is cancelled or no longer served here is a notice, never a next train.
  const operating = rows.filter((r) => !r.cancelled && !r.unservedHere)
  const next = operating.find((r) => r.platform === platform) ?? null

  const seen = new Set<string>()
  const notices = rows.filter((r) => {
    if (r === next || seen.has(r.key)) return false
    const here = r.plannedPlatform === platform || r.platform === platform
    const notable = r.cancelled || r.unservedHere || r.moved !== null
    if (here && notable) seen.add(r.key)
    return here && notable
  })
  return { next, notices }
}

/** The stops after this one, for the platform view's "calls at" list. */
export type CallingStop = { stopId: string; stationId: string; scheduledAt: number | null; served: boolean }

export function callingStops(journey: Journey, running: RunningResponse | null, fromStopId: string): CallingStop[] {
  const stops = [...journey.stops].sort((a, b) => a.sequence - b.sequence)
  const start = stops.findIndex((s) => s.stopId === fromStopId)
  return stops.slice(start + 1).map((s) => ({
    stopId: s.stopId,
    stationId: s.stationId,
    scheduledAt: ms(s.scheduledArrival) ?? ms(s.scheduledDeparture),
    served: running ? (running.stops.find((r) => r.stopId === s.stopId)?.served ?? true) && running.availability !== 'CANCELLED' : true,
  }))
}
