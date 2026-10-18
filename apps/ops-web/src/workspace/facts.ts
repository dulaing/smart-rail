import type { Evidence } from '../../../../shared/design/generated/status.ts'
import { freshnessOf } from '../../../../shared/design/freshness.ts'
import type { ReadFailure } from './api.ts'
import { minutesOf } from './time.ts'
import type { ConfirmedState, RunningResponse, Station } from './types.ts'

// Turns server answers into the few facts the screens show. Schedule, observation and confirmed
// change stay separate: each has its own function and none is derived from another.

/** How old the evidence is now, from the observation time (never from when the page last polled). */
export function evidenceOf(running: RunningResponse | null, nowMs: number): Evidence {
  return freshnessOf(running?.lastObservation?.observedAt, nowMs, running?.thresholds)
}

export type DelayFact =
  | { kind: 'unknown' }
  | { kind: 'on-time' }
  | { kind: 'late'; minutes: number }

/**
 * The delay the evidence supports. Unknown evidence gives no delay at all, so an old estimate is
 * never shown as "on time"; the API sends no delay then either, but evidence keeps ageing on the
 * page between polls, so the page checks again.
 */
export function delayOf(running: RunningResponse | null, evidence: Evidence): DelayFact {
  if (!running || evidence === 'UNKNOWN' || running.delaySeconds === null) return { kind: 'unknown' }
  const minutes = minutesOf(running.delaySeconds)
  return minutes > 0 ? { kind: 'late', minutes } : { kind: 'on-time' }
}

export function stationName(stations: readonly Station[], stationId: string | null | undefined): string {
  if (!stationId) return 'Unknown station'
  return stations.find((s) => s.id === stationId)?.nameEn ?? 'Unknown station'
}

/** The station the train last stood at or passed, from the newest observation, even if it is old. */
export function lastKnownStation(running: RunningResponse | null, stations: readonly Station[]): string | null {
  const id = running?.lastObservation?.lastKnownStationId
  return id ? stationName(stations, id) : null
}

export type ConfirmedFacts = {
  availability: RunningResponse['availability']
  /** The station an early ending stops at. */
  endsAt: string | null
  delayMinutes: number | null
  platformChanges: number
}

/** What staff have confirmed, as the running answer carries it. */
export function confirmedFacts(running: RunningResponse, stations: readonly Station[]): ConfirmedFacts {
  const finalStop = running.stops.find((s) => s.stopId === running.finalStopId)
  return {
    availability: running.availability,
    endsAt: finalStop ? stationName(stations, finalStop.stationId) : null,
    delayMinutes: running.confirmedDelayMinutes,
    platformChanges: running.stops.filter((s) => s.confirmedPlatform !== null).length,
  }
}

export function hasConfirmedChange(facts: ConfirmedFacts): boolean {
  return facts.availability !== 'AS_SCHEDULED' || facts.delayMinutes !== null || facts.platformChanges > 0
}

/** A sentence for one active change, for lists and for comparing before and after. */
export function describeActiveChange(
  change: ConfirmedState['activeChanges'][number],
  stopName: (stopId: string | null) => string,
): string {
  switch (change.kind) {
    case 'DELAY':
      return `Delay of ${change.delayMinutes ?? '?'} minutes`
    case 'CANCEL':
      return 'Cancellation'
    case 'PLATFORM':
      return `Platform ${change.newPlatform ?? '?'} at ${stopName(change.targetStopId)}`
    case 'EARLY_END':
      return `Ends early at ${stopName(change.finalStopId)}`
  }
}

/** Plain words for a read that failed. A lost connection is not a permission problem. */
export function failureText(failure: ReadFailure | null): string {
  if (!failure) return 'The information could not be read.'
  switch (failure.type) {
    case 'network':
      return 'Could not reach the API.'
    case 'unreadable':
      return 'The API sent an answer that could not be read.'
    case 'http':
      return `The API answered ${failure.status}.`
  }
}
