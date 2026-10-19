// The JSON of the Control report, incident and audit routes, written out by hand from the
// backend records (ReportDtos, IncidentDtos, AuditRecord). A name can be missing on the wire when
// the account behind an ID is gone, so names are nullable and the screens say "unknown person".

export type ReasonCode = 'TRAIN_FAULT' | 'SIGNAL_DELAY' | 'OBSTRUCTION' | 'CANNOT_CONTINUE'

export type ControlReport = {
  reportId: string
  journeyId: string
  reasonCode: string
  submittedById: string
  submittedByName: string | null
  receivedAt: string
  state: 'SENT_AWAITING_CONTROL' | 'ACKNOWLEDGED' | string
  acknowledgedById: string | null
  acknowledgedByName: string | null
  acknowledgedAt: string | null
}

export type ReportPage = { items: ControlReport[]; nextCursor: string | null }

export type Incident = {
  incidentId: string
  sourceReportId: string | null
  reasonCode: string
  state: 'OPEN' | 'RESOLVED' | string
  openedById: string
  openedByName: string | null
  openedAt: string
  resolvedAt: string | null
  changeIds: string[]
}

export type IncidentPage = { items: Incident[]; nextCursor: string | null }

export type AuditEntry = {
  id: string
  actorId: string
  actorName: string
  action: string
  outcome: string
  targetType: string
  targetId: string | null
  occurredAt: string
  correlationId: string
}

/** One oldest-first page of a trail. `complete` is true only when this page reached the end of the history. */
export type AuditPage = {
  entries: AuditEntry[]
  nextCursor: string | null
  complete: boolean
}
