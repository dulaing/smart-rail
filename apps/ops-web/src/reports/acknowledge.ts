import type { Fetcher } from '../workspace/api.ts'
import { acknowledgeReport, readReport, type CallResult } from './reportsApi.ts'
import type { ControlReport } from './types.ts'

// Acknowledging a report is one explicit write. This file decides what each answer means and
// never sends it twice: a lost answer is settled by reading the report, not by acknowledging
// again, and the name and time shown are always the server's, never made up on this page.

/** What a read of one report said. */
export type ReportRead =
  | { status: 'read'; report: ControlReport }
  | { status: 'missing' }
  | { status: 'denied'; code: 401 | 403 }
  | { status: 'failed' }

export function toRead(result: CallResult<ControlReport>): ReportRead {
  switch (result.kind) {
    case 'ok':
      return { status: 'read', report: result.body }
    case 'denied':
      return { status: 'denied', code: result.status }
    case 'rejected':
      return result.status === 404 ? { status: 'missing' } : { status: 'failed' }
    default:
      return { status: 'failed' }
  }
}

export type AckOutcome =
  /** The server recorded an acknowledgement. `byMe` is false when another controller got there first. */
  | { kind: 'acknowledged'; report: ControlReport; byMe: boolean }
  | { kind: 'denied'; code: 401 | 403 }
  | { kind: 'missing' }
  /** The server refused the request itself (not a conflict between controllers); nothing was recorded. */
  | { kind: 'refused'; detail: string }
  /** No usable answer. `check` is the one read made afterwards to see what the server holds now. */
  | { kind: 'uncertain'; why: 'network' | 'timeout' | 'server-error'; check: ReportRead }

/**
 * Sends the acknowledgement once. Whoever's it is, a 200 carries the winning acknowledgement:
 * `byMe` compares its controller with the signed-in account.
 */
export async function acknowledge(call: Fetcher, reportId: string, myAccountId: string): Promise<AckOutcome> {
  const result = await acknowledgeReport(call, reportId)
  switch (result.kind) {
    case 'ok':
      return { kind: 'acknowledged', report: result.body, byMe: result.body.acknowledgedById === myAccountId }
    case 'denied':
      return { kind: 'denied', code: result.status }
    case 'rejected':
      return result.status === 404 ? { kind: 'missing' } : { kind: 'refused', detail: `${result.title}: ${result.detail}` }
    default: {
      const why = result.kind === 'no-answer' ? result.reason : 'server-error'
      // The write may or may not have happened: read what the server holds, and send nothing more.
      return { kind: 'uncertain', why, check: toRead(await readReport(call, reportId)) }
    }
  }
}

/** Reads the report again after an uncertain acknowledgement. This is a read only. */
export async function recheck(call: Fetcher, reportId: string): Promise<ReportRead> {
  return toRead(await readReport(call, reportId))
}

export const isAcknowledged = (report: ControlReport): boolean =>
  report.acknowledgedAt !== null && report.acknowledgedById !== null

/**
 * The report to show: the newest word from the server. An acknowledgement, once recorded, never
 * changes, so any copy that has one is right and an older copy without one is not.
 */
export function settledReport(polled: ControlReport | null, outcome: AckOutcome | null): ControlReport | null {
  if (polled && isAcknowledged(polled)) return polled
  if (outcome?.kind === 'acknowledged') return outcome.report
  if (outcome?.kind === 'uncertain' && outcome.check.status === 'read' && (polled === null || isAcknowledged(outcome.check.report))) {
    return outcome.check.report
  }
  return polled
}
