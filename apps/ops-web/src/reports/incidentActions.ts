import type { Fetcher } from '../workspace/api.ts'
import {
  openIncidentFromReport,
  openStandaloneIncident,
  readIncident,
  resolveIncident,
  type CallResult,
} from './reportsApi.ts'
import type { Incident } from './types.ts'

// What each answer to an incident write means. Opening from a report is repeat-safe on the
// server (the same report always gives the same incident), so a screen may offer it again after
// a lost answer. A standalone incident is NOT repeat-safe: this file never sends one twice, and
// a lost answer is left uncertain for the person to settle by reading the list.

export type IncidentRead =
  | { status: 'read'; incident: Incident }
  | { status: 'missing' }
  | { status: 'denied'; code: 401 | 403 }
  | { status: 'failed' }

export function toIncidentRead(result: CallResult<Incident>): IncidentRead {
  switch (result.kind) {
    case 'ok':
      return { status: 'read', incident: result.body }
    case 'denied':
      return { status: 'denied', code: result.status }
    case 'rejected':
      return result.status === 404 ? { status: 'missing' } : { status: 'failed' }
    default:
      return { status: 'failed' }
  }
}

export const readIncidentOnce = async (call: Fetcher, incidentId: string): Promise<IncidentRead> =>
  toIncidentRead(await readIncident(call, incidentId))

export type Why = 'network' | 'timeout' | 'server-error'

export type OpenOutcome =
  | { kind: 'opened'; incident: Incident }
  | { kind: 'denied'; code: 401 | 403 }
  | { kind: 'missing' }
  | { kind: 'refused'; detail: string }
  | { kind: 'uncertain'; why: Why }

function sortOpen(result: CallResult<Incident>): OpenOutcome {
  switch (result.kind) {
    case 'ok':
      return { kind: 'opened', incident: result.body }
    case 'denied':
      return { kind: 'denied', code: result.status }
    case 'rejected':
      return result.status === 404
        ? { kind: 'missing' }
        : { kind: 'refused', detail: `${result.title}: ${result.detail}` }
    default:
      return { kind: 'uncertain', why: result.kind === 'no-answer' ? result.reason : 'server-error' }
  }
}

/** Opens (or finds) the incident for a report. Safe to send again: the server returns the same incident. */
export async function openFromReport(call: Fetcher, reportId: string): Promise<OpenOutcome> {
  return sortOpen(await openIncidentFromReport(call, reportId))
}

/** Opens a standalone incident, once. Never repeated for you, whatever the answer. */
export async function openStandalone(call: Fetcher, reasonCode: string): Promise<OpenOutcome> {
  return sortOpen(await openStandaloneIncident(call, reasonCode))
}

export type ResolveOutcome =
  /** The server holds it resolved; the time is the first resolution's. */
  | { kind: 'resolved'; incident: Incident }
  | { kind: 'denied'; code: 401 | 403 }
  | { kind: 'missing' }
  | { kind: 'refused'; detail: string }
  /** No usable answer; `check` is the one read made afterwards. Resolving twice is harmless but is not repeated for you. */
  | { kind: 'uncertain'; why: Why; check: IncidentRead }

export async function resolve(call: Fetcher, incidentId: string): Promise<ResolveOutcome> {
  const result = await resolveIncident(call, incidentId)
  switch (result.kind) {
    case 'ok':
      return { kind: 'resolved', incident: result.body }
    case 'denied':
      return { kind: 'denied', code: result.status }
    case 'rejected':
      return result.status === 404 ? { kind: 'missing' } : { kind: 'refused', detail: `${result.title}: ${result.detail}` }
    default:
      return {
        kind: 'uncertain',
        why: result.kind === 'no-answer' ? result.reason : 'server-error',
        check: await readIncidentOnce(call, incidentId),
      }
  }
}

/**
 * Is the incident named on a draft still open? true / false when the server says, null when it
 * could not be asked, which must never be treated as "resolved".
 */
export async function incidentStillOpen(call: Fetcher, incidentId: string): Promise<boolean | null> {
  const read = await readIncidentOnce(call, incidentId)
  if (read.status === 'read') return read.incident.state === 'OPEN'
  return read.status === 'missing' ? false : null
}

export const isResolved = (incident: Incident): boolean => incident.state === 'RESOLVED'
