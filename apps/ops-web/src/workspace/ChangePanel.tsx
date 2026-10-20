import { useEffect, useReducer, useRef, useState, type FormEvent, type RefObject } from 'react'
import { messages } from '../../../../shared/design/generated/status.ts'
import { ChangeReview } from './ChangeReview.tsx'
import { incidentStillOpen } from '../reports/incidentActions.ts'
import { reasonName } from '../reports/reasons.ts'
import { confirmReview, previewTarget, readCurrentState } from './changeActions.ts'
import { openIncidents } from './changesApi.ts'
import {
  flowReducer,
  initialFlow,
  type Draft,
  type FlowState,
  type Target,
} from './changeFlow.ts'
import { describeActiveChange, stationName } from './facts.ts'
import { usePolled } from './hooks.ts'
import { publicReader, staffCall } from './live.ts'
import { canChangePlatformAt, canWithdraw, startableKinds, type ChangeRights } from './rights.ts'
import { clockText } from './time.ts'
import type { ChangeKind, ConfirmedState, Incident, RunningResponse, Station } from './types.ts'
import './workspace.css'

const kindNames: Record<ChangeKind, string> = {
  DELAY: 'Delay',
  CANCEL: 'Cancellation',
  PLATFORM: 'Platform change',
  EARLY_END: 'Early ending',
}

const whyText = {
  network: 'the connection failed',
  timeout: 'the wait ran out',
  'server-error': 'the server gave an error or an answer that could not be read',
} as const

/**
 * Where a change is made: choose a type and details, preview it, review it, confirm it once.
 * It shows only what this person may start, but the API decides: a 403 ends the flow with an
 * access message. It never sends a confirmation twice, and never says a change was made unless
 * the API said so.
 */
export function ChangePanel({
  journeyId,
  running,
  changes,
  stations,
  rights,
  onChanged,
  onRefreshMe,
}: {
  journeyId: string
  running: RunningResponse
  changes: ConfirmedState
  stations: readonly Station[]
  rights: ChangeRights
  /** Read the journey again: after a write, a conflict or a lost answer. */
  onChanged: () => void
  onRefreshMe: () => void
}) {
  const [flow, dispatch] = useReducer(flowReducer, startableKinds(rights)[0] ?? 'DELAY', initialFlow)
  const [composeOpen, setComposeOpen] = useState(false)
  const sending = useRef(false)
  const headingRef = useRef<HTMLHeadingElement>(null)

  const stopLabel = (stopId: string | null) => {
    const stop = running.stops.find((s) => s.stopId === stopId)
    return stop ? `${stop.sequence}. ${stationName(stations, stop.stationId)}` : 'an unknown stop'
  }

  // Open incidents, only for someone allowed to name one. They are re-read with the journey so a
  // list that has gone stale is not what a person picks from.
  const { data: incidents } = usePolled<{ list: Incident[]; failed: boolean }>(
    `open-incidents:${rights.linkIncident}`,
    async () => {
      if (!rights.linkIncident) return { list: [], failed: false }
      const result = await openIncidents(staffCall)
      return result.kind === 'ok' ? { list: result.body, failed: false } : { list: [], failed: true }
    },
  )

  // If the incident named on the draft is resolved while the draft is open, drop any review of it.
  // A list that merely no longer shows it is checked with a read first; an unreadable answer is not "resolved".
  const linkedIncidentId = flow.target.type === 'publish' ? flow.target.draft.incidentId : ''
  useEffect(() => {
    if (!rights.linkIncident || linkedIncidentId === '' || incidents === null || incidents.failed) return
    if (incidents.list.some((i) => i.incidentId === linkedIncidentId)) return
    let cancelled = false
    void incidentStillOpen(staffCall, linkedIncidentId).then((open) => {
      if (!cancelled && open === false) dispatch({ type: 'incidentClosed', incidentId: linkedIncidentId })
    })
    return () => {
      cancelled = true
    }
  }, [incidents, linkedIncidentId, rights.linkIncident])

  // When a review, result or refusal appears, put keyboard and screen-reader focus on it.
  const stepName = flow.step.name
  useEffect(() => {
    if (stepName !== 'editing' && stepName !== 'previewing') headingRef.current?.focus()
  }, [stepName])

  const deps = { call: staffCall, reader: publicReader, journeyId, dispatch, onChanged }
  const runPreview = (target: Target) => previewTarget(deps, target, flow.lastRequestId + 1)
  const runConfirm = async () => {
    // One confirmation at a time, and only from a review that is still current.
    if (flow.step.name !== 'reviewing' || sending.current) return
    sending.current = true
    try {
      await confirmReview(deps, flow.step.review)
    } finally {
      sending.current = false
    }
  }

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    void runPreview(flow.target)
  }

  const busy = flow.step.name === 'previewing' || flow.step.name === 'confirming'
  const kinds = startableKinds(rights)
  const draft = flow.target.type === 'publish' ? flow.target.draft : null
  const incidentLabel = (id: string | null) => {
    const incident = incidents?.list.find((i) => i.incidentId === id)
    return id ? (incident ? `${reasonName(incident.reasonCode)}, opened ${clockText(incident.openedAt)}` : 'a linked incident') : null
  }

  return (
    <section className="panel change-workspace" aria-labelledby="change-heading">
      <h3 id="change-heading">
        {composeOpen || flow.step.name !== 'editing' ? 'Compose service change' : 'Current changes'}
      </h3>
      {flow.step.name === 'denied' ? (
        <div className="notice" role="alert">
          <p>
            {flow.step.status === 401
              ? 'Your session has ended, so nothing was sent or published.'
              : 'Your account is not allowed to make this change, or no longer is. Nothing was published.'}
          </p>
          <div className="actions">
            <button type="button" onClick={onRefreshMe}>
              Check my access again
            </button>
          </div>
        </div>
      ) : (
        <>
          {flow.notice && (
            <p className={`notice ${flow.notice.tone === 'warning' ? 'warning' : ''}`} role={flow.notice.tone === 'error' ? 'alert' : 'status'}>
              {flow.notice.text}
            </p>
          )}

          {draft && flow.step.name !== 'confirmed' && flow.step.name !== 'uncertain' && (composeOpen || flow.step.name !== 'editing') && (
            <DraftForm
              draft={draft}
              kinds={kinds}
              running={running}
              stations={stations}
              rights={rights}
              incidents={rights.linkIncident ? incidents : undefined}
              disabled={busy}
              previewing={flow.step.name === 'previewing'}
              onChange={(next) => dispatch({ type: 'edit', draft: next })}
              onSubmit={onSubmit}
            />
          )}

          {flow.step.name === 'reviewing' || flow.step.name === 'confirming' ? (
            <section className="review" aria-labelledby="review-heading">
              <h3 id="review-heading" tabIndex={-1} ref={headingRef}>
                Review: {flow.step.review.preview.action === 'WITHDRAW' ? 'withdraw ' : ''}
                {kindNames[flow.step.review.preview.change.kind].toLowerCase()}
              </h3>
              <p className="muted">
                Previewed at journey version {flow.step.review.preview.previewedVersion}. Nothing has been published yet.
                Changing anything above clears this review.
              </p>
              <ChangeReview
                preview={flow.step.review.preview}
                stations={stations}
                incidentLabel={incidentLabel(flow.step.review.preview.incidentId)}
              />
              <div className="actions">
                <button type="button" className="primary" disabled={busy} onClick={() => void runConfirm()}>
                  {flow.step.name === 'confirming'
                    ? 'Confirming…'
                    : flow.step.review.preview.action === 'WITHDRAW'
                      ? 'Confirm withdrawal'
                      : 'Confirm and publish'}
                </button>
                {flow.target.type === 'withdraw' && (
                  <button type="button" disabled={busy} onClick={() => dispatch({ type: 'reset' })}>
                    Do not withdraw
                  </button>
                )}
              </div>
              {flow.step.name === 'confirming' && (
                <p role="status" className="muted">
                  Waiting for SmartRail to answer. Do not send it again.
                </p>
              )}
            </section>
          ) : null}

          {flow.step.name === 'confirmed' && <Confirmed flow={flow} stopLabel={stopLabel} headingRef={headingRef} onReset={() => { dispatch({ type: 'reset' }); setComposeOpen(true) }} />}

          {flow.step.name === 'uncertain' && (
            <section className="notice warning" aria-labelledby="uncertain-heading">
              <h3 id="uncertain-heading" tabIndex={-1} ref={headingRef}>
                Outcome not confirmed
              </h3>
              <p role="alert">
                You confirmed this change, but no answer came back ({whyText[flow.step.why]}). SmartRail cannot tell
                whether it was recorded. It has not been sent again.
              </p>
              <Reconciliation
                step={flow.step}
                stopLabel={stopLabel}
                onCheck={() => void readCurrentState(deps)}
              />
              <div className="actions">
                <button type="button" className="primary" onClick={() => void runPreview(flow.target)}>
                  Preview again
                </button>
                <button type="button" onClick={() => { dispatch({ type: 'reset' }); setComposeOpen(false) }}>
                  Start a different change
                </button>
              </div>
              <p className="muted">
                Previewing again checks your change against the journey as it is now; it publishes nothing. You then
                confirm it as a new, separate action.
              </p>
            </section>
          )}

          {/* Changes in force, each with its own withdrawal. */}
          <ChangesInForce
            changes={changes}
            running={running}
            rights={rights}
            stopLabel={stopLabel}
            disabled={busy}
            onWithdraw={(changeId) => {
              dispatch({ type: 'withdraw', changeId })
              void runPreview({ type: 'withdraw', changeId })
            }}
          />
          {flow.step.name === 'editing' && !composeOpen && (
            <div className="change-empty-state">
              {changes.activeChanges.length === 0 && <p className="muted">No confirmed changes are in force.</p>}
              {kinds.length > 0 && (
                <button type="button" className="primary" onClick={() => setComposeOpen(true)}>
                  Compose change
                </button>
              )}
            </div>
          )}
        </>
      )}
    </section>
  )
}

function Confirmed({
  flow,
  stopLabel,
  headingRef,
  onReset,
}: {
  flow: FlowState
  stopLabel: (stopId: string | null) => string
  headingRef: RefObject<HTMLHeadingElement | null>
  onReset: () => void
}) {
  if (flow.step.name !== 'confirmed') return null
  const { result, review } = flow.step
  const withdrawn = review.preview.action === 'WITHDRAW'
  const mine = result.state.activeChanges.find((c) => c.changeId === result.changeId)
  return (
    <section className="notice success" aria-labelledby="done-heading">
      <h3 id="done-heading" tabIndex={-1} ref={headingRef}>
        {withdrawn ? 'Withdrawal confirmed' : 'Change confirmed'}
      </h3>
      <p role="status">
        SmartRail recorded this at journey version {result.journeyVersion}.
        {mine && ` Public screens show: ${messages['change.confirmed'].text.en.replace('{time}', clockText(mine.confirmedAt))}.`}
      </p>
      <p className="muted">
        This is the recorded decision. It does not say that every passenger app, board or food partner has received it.
      </p>
      <h4>Now in force</h4>
      {result.state.activeChanges.length === 0 ? (
        <p>No confirmed changes.</p>
      ) : (
        <ul>
          {result.state.activeChanges.map((change) => (
            <li key={change.changeId}>{describeActiveChange(change, stopLabel)}</li>
          ))}
        </ul>
      )}
      <div className="actions">
        <button type="button" onClick={onReset}>
          Make another change
        </button>
      </div>
    </section>
  )
}

function Reconciliation({
  step,
  stopLabel,
  onCheck,
}: {
  step: Extract<FlowState['step'], { name: 'uncertain' }>
  stopLabel: (stopId: string | null) => string
  onCheck: () => void
}) {
  const { reconciliation, review } = step
  if (reconciliation.status === 'reading') return <p role="status">Reading what is confirmed now…</p>
  if (reconciliation.status === 'failed') {
    return (
      <div>
        <p role="alert">The current state could not be read either, so it is still not known what happened.</p>
        <div className="actions">
          <button type="button" onClick={onCheck}>
            Read again
          </button>
        </div>
      </div>
    )
  }
  const { state } = reconciliation
  const previewed = review.preview.previewedVersion
  return (
    <div>
      <h4>What is confirmed now</h4>
      <p>
        The journey is at version {state.journeyVersion}; your preview was at version {previewed}.{' '}
        {state.journeyVersion === previewed
          ? 'It has not moved on, so no change has been recorded since your preview.'
          : 'It has moved on, but this page cannot tell whether your change or someone else’s made it do so. Compare the list below with what you meant to do.'}
      </p>
      {state.activeChanges.length === 0 ? (
        <p>No confirmed changes are in force.</p>
      ) : (
        <ul>
          {state.activeChanges.map((change) => (
            <li key={change.changeId}>
              {describeActiveChange(change, stopLabel)}, confirmed {clockText(change.confirmedAt)}
            </li>
          ))}
        </ul>
      )}
      <div className="actions">
        <button type="button" onClick={onCheck}>
          Read again
        </button>
      </div>
    </div>
  )
}

function ChangesInForce({
  changes,
  running,
  rights,
  stopLabel,
  disabled,
  onWithdraw,
}: {
  changes: ConfirmedState
  running: RunningResponse
  rights: ChangeRights
  stopLabel: (stopId: string | null) => string
  disabled: boolean
  onWithdraw: (changeId: string) => void
}) {
  const allowed = changes.activeChanges.filter((change) => {
    const stop = running.stops.find((s) => s.stopId === change.targetStopId)
    return canWithdraw(rights, change, stop?.stationId ?? null)
  })
  if (allowed.length === 0) return null
  return (
    <section aria-labelledby="withdraw-heading">
      <h3 id="withdraw-heading">Withdraw a change in force</h3>
      <ul className="plain">
        {allowed.map((change) => (
          <li key={change.changeId} className="line">
            <span>
              {describeActiveChange(change, stopLabel)}
              <span className="sub">Confirmed {clockText(change.confirmedAt)}</span>
            </span>
            <button type="button" disabled={disabled} onClick={() => onWithdraw(change.changeId)}>
              Review withdrawal
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}

function DraftForm({
  draft,
  kinds,
  running,
  stations,
  rights,
  incidents,
  disabled,
  previewing,
  onChange,
  onSubmit,
}: {
  draft: Draft
  kinds: ChangeKind[]
  running: RunningResponse
  stations: readonly Station[]
  rights: ChangeRights
  /** undefined: this person cannot name an incident. */
  incidents: { list: Incident[]; failed: boolean } | null | undefined
  disabled: boolean
  previewing: boolean
  onChange: (draft: Draft) => void
  onSubmit: (event: FormEvent) => void
}) {
  const set = (patch: Partial<Draft>) => onChange({ ...draft, ...patch })
  const platformStops = running.stops.filter((s) => canChangePlatformAt(rights, s.stationId))
  // An early ending needs an intermediate stop the train is scheduled to arrive at.
  const endStops = running.stops.slice(1, -1).filter((s) => s.scheduledArrival !== null)
  const label = (stop: { sequence: number; stationId: string }) => `${stop.sequence}. ${stationName(stations, stop.stationId)}`

  return (
    <form onSubmit={onSubmit} className="form" aria-label="Change details">
      <fieldset disabled={disabled}>
        <legend>Type of change</legend>
        <div className="choices">
          {kinds.map((kind) => (
            <label key={kind} className="choice">
              <input
                type="radio"
                name="kind"
                value={kind}
                checked={draft.kind === kind}
                // Details of another type must not travel with this one.
                onChange={() => onChange({ ...draft, kind, delayMinutes: '', newPlatform: '', targetStopId: '', finalStopId: '' })}
              />
              {kindNames[kind]}
            </label>
          ))}
        </div>
      </fieldset>

      <fieldset disabled={disabled} className="fields">
        <legend>Details</legend>
        {draft.kind === 'DELAY' && (
          <label>
            Delay in minutes
            <input
              type="number"
              inputMode="numeric"
              min={0}
              step={1}
              value={draft.delayMinutes}
              onChange={(e) => set({ delayMinutes: e.target.value })}
            />
          </label>
        )}
        {draft.kind === 'CANCEL' && <p className="muted">Cancels the whole journey. There is nothing else to enter.</p>}
        {draft.kind === 'PLATFORM' && (
          <>
            <label>
              Stop
              <select value={draft.targetStopId} onChange={(e) => set({ targetStopId: e.target.value })}>
                <option value="">Choose a stop</option>
                {platformStops.map((stop) => (
                  <option key={stop.stopId} value={stop.stopId}>
                    {label(stop)}
                    {stop.plannedPlatform ? ` (planned platform ${stop.plannedPlatform})` : ''}
                  </option>
                ))}
              </select>
            </label>
            <label>
              New platform
              <input type="text" value={draft.newPlatform} onChange={(e) => set({ newPlatform: e.target.value })} />
            </label>
          </>
        )}
        {draft.kind === 'EARLY_END' && (
          <label>
            Train ends at
            <select value={draft.finalStopId} onChange={(e) => set({ finalStopId: e.target.value })}>
              <option value="">Choose a stop</option>
              {endStops.map((stop) => (
                <option key={stop.stopId} value={stop.stopId}>
                  {label(stop)}
                </option>
              ))}
            </select>
          </label>
        )}
        {incidents !== undefined && (
          <label>
            Incident (optional)
            <select value={draft.incidentId} onChange={(e) => set({ incidentId: e.target.value })}>
              <option value="">No incident linked</option>
              {/* A link kept after its incident left the open list stays visible so it can be removed or changed. */}
              {draft.incidentId !== '' && !(incidents?.list ?? []).some((i) => i.incidentId === draft.incidentId) && (
                <option value={draft.incidentId}>The linked incident (no longer shown as open)</option>
              )}
              {(incidents?.list ?? []).map((incident) => (
                <option key={incident.incidentId} value={incident.incidentId}>
                  {reasonName(incident.reasonCode)}, opened {clockText(incident.openedAt)}
                </option>
              ))}
            </select>
            {incidents?.failed && <span className="sub problem">Open incidents could not be read. You can still preview without one.</span>}
          </label>
        )}
      </fieldset>

      <div className="actions">
        <button type="submit" className="primary" disabled={disabled}>
          {previewing ? 'Previewing…' : 'Preview change'}
        </button>
      </div>
    </form>
  )
}
