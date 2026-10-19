import { useEffect, useRef, useState } from 'react'
import { canOpenPath } from '../access.ts'
import type { Me } from '../auth'
import { AppLink } from '../workspace/parts.tsx'
import { usePolled } from '../workspace/hooks.ts'
import { staffCall } from '../workspace/live.ts'
import { acknowledge, isAcknowledged, recheck, settledReport, toRead, type AckOutcome } from './acknowledge.ts'
import { openFromReport, type OpenOutcome } from './incidentActions.ts'
import { dateTimeText, isGuid, personName } from './format.ts'
import { journeyLabel, type JourneyChoice } from './journeyChoices.ts'
import { reasonName } from './reasons.ts'
import { readReport } from './reportsApi.ts'
import type { ControlReport } from './types.ts'

type Detail = {
  report: ControlReport | null
  status: 'read' | 'missing' | 'denied' | 'failed'
  deniedCode: 401 | 403 | null
  /** A report was read on this page earlier, so a later 404 means it has gone, not that the address is wrong. */
  shownBefore: boolean
}

type AckStep = { name: 'idle' } | { name: 'sending' } | { name: 'done'; outcome: AckOutcome }

/**
 * One report. Opening it is a read. The Acknowledge button is the only thing here that writes: it
 * sends once, is disabled while it waits, and shows the server's name and time, whether they are
 * yours or those of the controller who got there first. A lost answer is settled by reading the
 * report again; it is never acknowledged a second time on your behalf.
 */
export function ReportDetail({
  reportId,
  me,
  choices,
  inList,
  listLoaded,
  onReport,
  onRefreshMe,
}: {
  reportId: string
  me: Me
  choices: readonly JourneyChoice[]
  inList: boolean
  listLoaded: boolean
  /** Tells the inbox the newest state of this report. */
  onReport: (report: ControlReport) => void
  onRefreshMe: () => void
}) {
  const valid = isGuid(reportId)
  const [ack, setAck] = useState<AckStep>({ name: 'idle' })
  const sending = useRef(false)
  const headingRef = useRef<HTMLHeadingElement>(null)

  const { data, refresh } = usePolled<Detail>(`report:${valid ? reportId : 'invalid'}`, async (signal, previous) => {
    if (!valid) return { report: null, status: 'missing', deniedCode: null, shownBefore: false }
    const read = toRead(await readReport(staffCall, reportId))
    if (signal.aborted) return previous ?? { report: null, status: 'failed', deniedCode: null, shownBefore: false }
    const earlier = previous?.report ?? null
    if (read.status === 'read') {
      onReport(read.report)
      return { report: read.report, status: 'read', deniedCode: null, shownBefore: true }
    }
    return {
      report: read.status === 'missing' ? null : earlier,
      status: read.status,
      deniedCode: read.status === 'denied' ? read.code : null,
      shownBefore: previous?.shownBefore ?? false,
    }
  })

  const outcome = ack.name === 'done' ? ack.outcome : null
  const shown = settledReport(data?.report ?? null, outcome)

  // Move focus to the report when it opens, so the choice made in the list is announced.
  const ready = shown !== null
  useEffect(() => {
    if (ready) headingRef.current?.focus()
  }, [ready, reportId])

  const send = async () => {
    // One acknowledgement at a time, from this click only.
    if (sending.current || shown === null) return
    sending.current = true
    setAck({ name: 'sending' })
    try {
      const result = await acknowledge(staffCall, reportId, me.accountId)
      setAck({ name: 'done', outcome: result })
      if (result.kind === 'acknowledged') onReport(result.report)
      if (result.kind === 'uncertain' && result.check.status === 'read') onReport(result.check.report)
      if (result.kind === 'acknowledged') refresh()
    } finally {
      sending.current = false
    }
  }

  const checkAgain = async () => {
    if (outcome?.kind !== 'uncertain' || sending.current) return
    sending.current = true
    try {
      const check = await recheck(staffCall, reportId)
      setAck({ name: 'done', outcome: { ...outcome, check } })
      if (check.status === 'read') onReport(check.report)
    } finally {
      sending.current = false
    }
  }

  if (!valid || data?.status === 'missing') {
    return (
      <div className="notice" role="alert">
        <p>
          {data?.shownBefore
            ? 'This report is no longer available. It was shown earlier on this page but the server no longer has it.'
            : 'No report has this ID.'}
        </p>
        <AppLink to="/reports">Back to the inbox</AppLink>
      </div>
    )
  }

  const deniedCode = data?.status === 'denied' ? data.deniedCode : outcome?.kind === 'denied' ? outcome.code : null
  if (deniedCode !== null) {
    return (
      <div className="notice" role="alert">
        <p>
          {deniedCode === 401
            ? 'Your session has ended. Nothing was acknowledged.'
            : 'Your account is not allowed to read or acknowledge reports, or no longer is. Nothing was acknowledged.'}
        </p>
        <div className="actions">
          <button type="button" onClick={onRefreshMe}>
            Check my access again
          </button>
        </div>
      </div>
    )
  }

  if (shown === null) {
    return data === null ? (
      <p role="status" className="muted">
        Loading the report…
      </p>
    ) : (
      <div className="notice" role="alert">
        <p>The report could not be read. This is a connection or server problem, not a permission problem.</p>
        <div className="actions">
          <button type="button" onClick={refresh}>
            Try again
          </button>
        </div>
      </div>
    )
  }

  const acknowledged = isAcknowledged(shown)
  const uncertain = outcome?.kind === 'uncertain' ? outcome : null
  // After a lost answer the button comes back only once a read shows the server has no acknowledgement.
  const unsettled = uncertain !== null && uncertain.check.status !== 'read' && !acknowledged
  const journey = journeyLabel(choices, shown.journeyId)
  const journeyTo = `/corridor/journeys/${shown.journeyId}`

  return (
    <article className="panel" aria-labelledby="report-heading">
      <h2 id="report-heading" tabIndex={-1} ref={headingRef}>
        {reasonName(shown.reasonCode)}
      </h2>

      {data?.status === 'failed' && (
        <div className="notice warning" role="status">
          <p>This report could not be refreshed, so what is shown may be out of date.</p>
          <div className="actions">
            <button type="button" onClick={refresh}>
              Try again
            </button>
          </div>
        </div>
      )}
      {listLoaded && !inList && (
        <p className="sub">This report is not in the list as it is filtered now. It is still open here.</p>
      )}

      <dl className="facts">
        <div>
          <dt>Driver</dt>
          <dd>{personName(shown.submittedByName)}</dd>
        </div>
        <div>
          <dt>Journey</dt>
          <dd>{canOpenPath(me, journeyTo) ? <AppLink to={journeyTo}>{journey}</AppLink> : journey}</dd>
        </div>
        <div>
          <dt>Reason</dt>
          <dd>{reasonName(shown.reasonCode)}</dd>
        </div>
        <div>
          <dt>Received by SmartRail</dt>
          <dd>{dateTimeText(shown.receivedAt)}</dd>
        </div>
      </dl>
      <p className="muted">Receipt means the report reached SmartRail. It is not an acknowledgement.</p>

      <section aria-labelledby="ack-heading" className="stack tight">
        <h3 id="ack-heading">Acknowledgement</h3>
        {acknowledged && shown.acknowledgedAt !== null ? (
          <div className="notice success" role="status">
            <p>
              {outcome?.kind === 'acknowledged' && outcome.byMe
                ? 'You acknowledged this report. '
                : outcome?.kind === 'acknowledged'
                  ? 'Another controller had already acknowledged this report, so your click changed nothing. '
                  : ''}
              Acknowledged by <strong>{personName(shown.acknowledgedByName)}</strong> at{' '}
              <strong>{dateTimeText(shown.acknowledgedAt)}</strong>. This is the first acknowledgement recorded by SmartRail.
            </p>
          </div>
        ) : (
          <div className="notice neutral">
            <p>Nobody has acknowledged this report yet.</p>
          </div>
        )}

        {outcome?.kind === 'refused' && (
          <div className="notice" role="alert">
            <p>The acknowledgement was refused: {outcome.detail} Nothing was recorded.</p>
          </div>
        )}
        {uncertain && !acknowledged && (
          <div className="notice warning" role="alert">
            <p>
              <strong>Not confirmed.</strong> No answer came back ({whyText[uncertain.why]}).{' '}
              {uncertain.check.status === 'read'
                ? 'SmartRail was then asked directly and shows no acknowledgement for this report. You can acknowledge it again.'
                : 'SmartRail could not tell whether it was recorded, and has not been asked again. Check again before acknowledging.'}
            </p>
          </div>
        )}
        {uncertain && acknowledged && (
          <p className="sub">
            No answer came back to your click, so this was settled by reading the report. SmartRail does not say whose click
            it was.
          </p>
        )}

        {!acknowledged && (
          <div className="actions">
            {unsettled ? (
              <button type="button" className="primary" onClick={() => void checkAgain()}>
                Check what SmartRail recorded
              </button>
            ) : (
              <button type="button" className="primary" disabled={ack.name === 'sending'} onClick={() => void send()}>
                {ack.name === 'sending' ? 'Acknowledging…' : 'Acknowledge report'}
              </button>
            )}
          </div>
        )}
        {ack.name === 'sending' && (
          <p role="status" className="muted">
            Waiting for SmartRail to answer. Do not send it again.
          </p>
        )}
        <p className="sub">Acknowledging does not open an incident or publish a change.</p>
      </section>

      <ReportIncident reportId={reportId} onRefreshMe={onRefreshMe} />
    </article>
  )
}

const whyText = {
  network: 'the connection failed',
  timeout: 'the wait ran out',
  'server-error': 'the server gave an error',
} as const

type IncidentStep = { name: 'idle' } | { name: 'sending' } | { name: 'done'; outcome: OpenOutcome }

/**
 * Starts an incident from this report. It is its own action: it does not acknowledge the report
 * (before or after) and publishes nothing. Opening twice from one report gives the same
 * incident, so after a lost answer trying again is safe and the page says so.
 */
function ReportIncident({ reportId, onRefreshMe }: { reportId: string; onRefreshMe: () => void }) {
  const [step, setStep] = useState<IncidentStep>({ name: 'idle' })
  const sending = useRef(false)

  const open = async () => {
    if (sending.current) return
    sending.current = true
    setStep({ name: 'sending' })
    try {
      setStep({ name: 'done', outcome: await openFromReport(staffCall, reportId) })
    } finally {
      sending.current = false
    }
  }

  const outcome = step.name === 'done' ? step.outcome : null
  return (
    <section aria-labelledby="incident-heading" className="stack tight">
      <h3 id="incident-heading">Incident</h3>
      {outcome?.kind === 'opened' ? (
        <div className="notice success" role="status">
          <p>
            An incident is open for this report: <AppLink to={`/incidents/${outcome.incident.incidentId}`}>view the incident</AppLink>.
            Opening it again from this report returns the same incident, never a second one.
          </p>
        </div>
      ) : (
        <p className="muted">Open an incident for this report to track it to a resolution. This is separate from acknowledging it.</p>
      )}
      {outcome?.kind === 'refused' && (
        <div className="notice" role="alert">
          <p>No incident was opened: {outcome.detail}</p>
        </div>
      )}
      {outcome?.kind === 'missing' && (
        <div className="notice" role="alert">
          <p>This report no longer exists, so no incident was opened.</p>
        </div>
      )}
      {outcome?.kind === 'uncertain' && (
        <div className="notice warning" role="alert">
          <p>
            <strong>Not confirmed.</strong> No answer came back. Opening an incident from a report is safe to repeat: if one
            was created, you will get that same incident.
          </p>
        </div>
      )}
      {outcome?.kind === 'denied' && (
        <div className="notice" role="alert">
          <p>
            {outcome.code === 401
              ? 'Your session has ended. No incident was opened.'
              : 'Your account is not allowed to open incidents, or no longer is. No incident was opened.'}
          </p>
          <div className="actions">
            <button type="button" onClick={onRefreshMe}>
              Check my access again
            </button>
          </div>
        </div>
      )}
      {outcome?.kind !== 'opened' && (
        <div className="actions">
          <button type="button" disabled={step.name === 'sending'} onClick={() => void open()}>
            {step.name === 'sending' ? 'Opening…' : 'Open incident from this report'}
          </button>
        </div>
      )}
    </section>
  )
}
