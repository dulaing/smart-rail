import type { Fetcher } from '../workspace/api.ts'
import { post, send, type CallResult } from '../workspace/changesApi.ts'
import type { AuditPage, ControlReport, Incident, IncidentPage, ReportPage } from './types.ts'

// The staff calls behind the report inbox, incidents and audit history. Each answer is sorted by
// `send` into ok / rejected / denied / no-answer / server-error, so a screen can tell a refusal
// from a lost connection. Nothing here sends a request twice.

export type { CallResult }

/** How many reports or incidents one page asks for. The server allows up to 100. */
export const pageSize = 25

/** How many audit entries one page asks for. The server allows up to 200. */
export const auditPageSize = 50

export type AckFilter = 'no' | 'yes' | 'all'

export type InboxFilter = {
  acknowledged: AckFilter
  /** Only reports sent on this journey; null for every journey. */
  journeyId: string | null
}

export const defaultFilter: InboxFilter = { acknowledged: 'no', journeyId: null }

function query(values: Record<string, string | number | null | undefined>): string {
  const parts = Object.entries(values)
    .filter((entry): entry is [string, string | number] => entry[1] !== null && entry[1] !== undefined)
    .map(([name, value]) => `${name}=${encodeURIComponent(String(value))}`)
  return parts.length === 0 ? '' : `?${parts.join('&')}`
}

/** The query of one inbox page. The cursor is opaque and sent back exactly as the server gave it. */
export function reportsQuery(filter: InboxFilter, cursor: string | null): string {
  return query({
    acknowledged: filter.acknowledged === 'all' ? null : filter.acknowledged === 'yes' ? 'true' : 'false',
    journeyId: filter.journeyId,
    limit: pageSize,
    cursor,
  })
}

const get: RequestInit = { method: 'GET' }

export const listReports = (call: Fetcher, filter: InboxFilter, cursor: string | null) =>
  send<ReportPage>(call, `/api/operations/reports${reportsQuery(filter, cursor)}`, get)

/** Reading a report is only a read. It never acknowledges anything. */
export const readReport = (call: Fetcher, reportId: string) =>
  send<ControlReport>(call, `/api/operations/reports/${reportId}`, get)

/** The only call that acknowledges a report. A repeat returns the first controller's recorded result. */
export const acknowledgeReport = (call: Fetcher, reportId: string) =>
  send<ControlReport>(call, `/api/operations/reports/${reportId}/acknowledge`, { method: 'POST' })

export type IncidentFilter = 'OPEN' | 'RESOLVED'

export const listIncidents = (call: Fetcher, state: IncidentFilter, cursor: string | null) =>
  send<IncidentPage>(call, `/api/operations/incidents${query({ state, limit: pageSize, cursor })}`, get)

export const readIncident = (call: Fetcher, incidentId: string) =>
  send<Incident>(call, `/api/operations/incidents/${incidentId}`, get)

/** Opens an incident from a report. Opening twice from the same report returns the same incident. */
export const openIncidentFromReport = (call: Fetcher, sourceReportId: string) =>
  send<Incident>(call, '/api/operations/incidents', post({ sourceReportId }))

/** Opens a standalone incident. The server does not de-duplicate these, so callers must never replay one blindly. */
export const openStandaloneIncident = (call: Fetcher, reasonCode: string) =>
  send<Incident>(call, '/api/operations/incidents', post({ reasonCode }))

/** Resolves an incident. It touches no report acknowledgement and no published change. */
export const resolveIncident = (call: Fetcher, incidentId: string) =>
  send<Incident>(call, `/api/operations/incidents/${incidentId}/resolve`, { method: 'POST' })

export type AuditSubject = { type: 'journey' | 'incident'; id: string }

const auditRoute = (subject: AuditSubject) =>
  subject.type === 'journey'
    ? `/api/operations/journeys/${subject.id}/audit`
    : `/api/operations/incidents/${subject.id}/audit`

export const readAudit = (call: Fetcher, subject: AuditSubject, cursor: string | null) =>
  send<AuditPage & Record<string, unknown>>(call, `${auditRoute(subject)}${query({ limit: auditPageSize, cursor })}`, get)
