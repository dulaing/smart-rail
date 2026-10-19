import { useEffect, useReducer, useRef, useState, type MouseEvent } from 'react'
import { canOpenPath } from '../access.ts'
import type { Me } from '../auth'
import { navigate } from '../router.ts'
import { usePolled } from '../workspace/hooks.ts'
import { staffCall } from '../workspace/live.ts'
import { AppLink } from '../workspace/parts.tsx'
import { clockText } from '../workspace/time.ts'
import { dateTimeText, isGuid, personName } from './format.ts'
import { openStandalone, readIncidentOnce, resolve, type OpenOutcome, type ResolveOutcome, isResolved } from './incidentActions.ts'
import { emptyPaged, pagedReducer, visibleItems } from './paged.ts'
import { reasonCodes, reasonName } from './reasons.ts'
import { listIncidents, type IncidentFilter } from './reportsApi.ts'
import type { Incident } from './types.ts'
import './reports.css'

const incidentReducer = pagedReducer<Incident>((i) => i.incidentId)
const idOf = (i: Incident) => i.incidentId

type Status =
  | { kind: 'ok'; readAt: number }
  | { kind: 'failed'; lastOk: number | null }
  | { kind: 'denied'; code: 401 | 403 }

const incidentPath = /^\/incidents\/([^/]+)$/

/**
 * Incidents: the Open/Resolved list, one incident beside it, and (for incidents.manage) opening a
 * standalone incident and resolving one. Auditors get the same reading screens with no write
 * controls. Resolving never acknowledges a report or withdraws a change.
 */
export function IncidentWorkspace({ me, path, onRefreshMe }: { me: Me; path: string; onRefreshMe: () => void }) {
  const match = incidentPath.exec(path)
  if (path !== '/incidents' && !match) {
    return (
      <p className="notice" role="alert">
        There is no page at this address.
      </p>
    )
  }
  return <IncidentScreen key={me.accountId} me={me} selectedId={match ? match[1] : null} onRefreshMe={onRefreshMe} />
}

function IncidentScreen({ me, selectedId, onRefreshMe }: { me: Me; selectedId: string | null; onRefreshMe: () => void }) {
  const canManage = me.permissions.some((p) => p.code === 'incidents.manage')
  const [state, setState] = useState<IncidentFilter>('OPEN')
  const [list, dispatch] = useReducer(incidentReducer, state, (s) => emptyPaged<Incident>(s))
  const loadingMore = useRef(false)

  const { data: status, refresh } = usePolled<Status>(`incidents:${state}`, async (signal, previous) => {
    const result = await listIncidents(staffCall, state, null)
    if (signal.aborted) return previous ?? { kind: 'failed', lastOk: null }
    if (result.kind === 'ok') {
      dispatch({ type: 'firstPage', key: state, page: result.body })
      return { kind: 'ok', readAt: Date.now() }
    }
    if (result.kind === 'denied') return { kind: 'denied', code: result.status }
    return { kind: 'failed', lastOk: previous?.kind === 'ok' ? previous.readAt : previous?.kind === 'failed' ? previous.lastOk : null }
  })

  const changeState = (next: IncidentFilter) => {
    setState(next)
    dispatch({ type: 'key', key: next })
  }

  const loadMore = async () => {
    const from = list.tail
    if (from === null || loadingMore.current) return
    loadingMore.current = true
    dispatch({ type: 'moreStarted', key: state, from })
    try {
      const result = await listIncidents(staffCall, state, from)
      dispatch(result.kind === 'ok' ? { type: 'morePage', key: state, from, page: result.body } : { type: 'moreFailed', key: state, from })
    } finally {
      loadingMore.current = false
    }
  }

  const incidents = visibleItems(list, idOf)
  const selectedInList = selectedId !== null && incidents.some((i) => i.incidentId === selectedId)

  return (
    <div className="reports-layout">
      <section className="stack tight" aria-labelledby="incident-list-heading">
        <h2 id="incident-list-heading">Incidents</h2>
        <p className="muted">Newest first, refreshed every 15 seconds while this page is open.</p>

        {canManage && (
          <OpenIncidentForm
            onOpened={(incident) => {
              refresh()
              navigate(`/incidents/${incident.incidentId}`)
            }}
            onCheckList={async () => {
              // After a lost answer the list is read again, as one explicit step, before another incident can be opened.
              changeState('OPEN')
              const result = await listIncidents(staffCall, 'OPEN', null)
              if (result.kind !== 'ok') return false
              dispatch({ type: 'reload' })
              dispatch({ type: 'firstPage', key: 'OPEN', page: result.body })
              refresh()
              return true
            }}
            onRefreshMe={onRefreshMe}
          />
        )}

        <form className="filters" aria-label="Filter incidents" onSubmit={(e) => e.preventDefault()}>
          <label>
            Show
            <select value={state} onChange={(e) => changeState(e.target.value as IncidentFilter)}>
              <option value="OPEN">Open</option>
              <option value="RESOLVED">Resolved</option>
            </select>
          </label>
        </form>

        {status?.kind === 'denied' ? (
          <div className="notice" role="alert">
            <p>
              {status.code === 401
                ? 'Your session has ended, so incidents cannot be read.'
                : 'Your account is not allowed to read incidents, or no longer is.'}
            </p>
            <div className="actions">
              <button type="button" onClick={onRefreshMe}>
                Check my access again
              </button>
            </div>
          </div>
        ) : !list.loaded && (status === null || status.kind === 'ok') ? (
          <p role="status" className="muted">
            Loading incidents…
          </p>
        ) : !list.loaded ? (
          <div className="notice" role="alert">
            <p>Incidents could not be read. This is a connection or server problem, not a permission problem.</p>
            <div className="actions">
              <button type="button" onClick={refresh}>
                Try again
              </button>
            </div>
          </div>
        ) : (
          <>
            {status?.kind === 'failed' && (
              <div className="notice warning" role="status">
                <p>
                  The list could not be refreshed
                  {status.lastOk !== null ? `; this is the list as of ${clockText(status.lastOk)}` : ''}.
                </p>
                <div className="actions">
                  <button type="button" onClick={refresh}>
                    Try again
                  </button>
                </div>
              </div>
            )}
            {incidents.length === 0 ? (
              <div className="notice neutral" role="status">
                <p>{state === 'OPEN' ? 'No incident is open.' : 'No incident has been resolved.'}</p>
              </div>
            ) : (
              <ul className="plain report-list" aria-label="Incidents">
                {incidents.map((incident) => (
                  <IncidentRow key={incident.incidentId} incident={incident} selected={incident.incidentId === selectedId} />
                ))}
              </ul>
            )}
            {list.extended && (
              <p className="sub">
                Incidents below the newest page were read earlier and may be out of date.{' '}
                <button type="button" onClick={() => dispatch({ type: 'reload' })}>
                  Reload the list
                </button>
              </p>
            )}
            {list.tail !== null ? (
              <div className="actions">
                <button type="button" disabled={list.loadingFrom !== null} onClick={() => void loadMore()}>
                  {list.loadingFrom !== null ? 'Loading…' : 'Load more incidents'}
                </button>
                {list.loadMoreFailed && (
                  <span role="alert" className="problem">
                    More incidents could not be read. Try again.
                  </span>
                )}
              </div>
            ) : (
              incidents.length > 0 && <p className="sub">That is every incident for this filter.</p>
            )}
          </>
        )}
      </section>

      <section aria-label="Incident detail" className="stack tight">
        {selectedId === null ? (
          <div className="notice neutral">
            <p>Choose an incident to read it.</p>
          </div>
        ) : (
          <IncidentDetail
            key={selectedId}
            incidentId={selectedId}
            me={me}
            canManage={canManage}
            inList={selectedInList}
            listLoaded={list.loaded}
            onIncident={(incident) => dispatch({ type: 'patch', item: incident })}
            onRefreshMe={onRefreshMe}
          />
        )}
      </section>
    </div>
  )
}

function IncidentRow({ incident, selected }: { incident: Incident; selected: boolean }) {
  const to = `/incidents/${incident.incidentId}`
  const open = (event: MouseEvent<HTMLAnchorElement>) => {
    if (event.metaKey || event.ctrlKey || event.shiftKey || event.button !== 0) return
    event.preventDefault()
    navigate(to)
  }
  const resolved = isResolved(incident)
  return (
    <li>
      <a href={to} onClick={open} className={`report-row${selected ? ' selected' : ''}`} aria-current={selected ? 'true' : undefined}>
        <span className="row-title">
          <strong>{reasonName(incident.reasonCode)}</strong>
          <span className={`sr-badge ${resolved ? 'sr-tone-neutral' : 'sr-tone-warning'}`}>{resolved ? 'Resolved' : 'Open'}</span>
        </span>
        <span className="sub">
          Opened by {personName(incident.openedByName)} at {dateTimeText(incident.openedAt)}
        </span>
        {resolved && incident.resolvedAt !== null && <span className="sub">Resolved at {dateTimeText(incident.resolvedAt)}</span>}
      </a>
    </li>
  )
}

type CreateStep =
  | { name: 'idle' }
  | { name: 'sending' }
  | { name: 'refused'; detail: string }
  | { name: 'denied'; code: 401 | 403 }
  /** No answer. The incident may exist, and the server would not recognise a second attempt as the same one. */
  | { name: 'uncertain'; why: 'network' | 'timeout' | 'server-error'; checked: boolean; checking: boolean; checkFailed: boolean }

const whyText = {
  network: 'the connection failed',
  timeout: 'the wait ran out',
  'server-error': 'the server gave an error',
} as const

/**
 * Opens an incident that stands alone, from a reason code. It sends once per click and is
 * disabled while it waits. A lost answer is never replayed: the person reads the list again
 * before another incident can be opened.
 */
function OpenIncidentForm({
  onOpened,
  onCheckList,
  onRefreshMe,
}: {
  onOpened: (incident: Incident) => void
  onCheckList: () => Promise<boolean>
  onRefreshMe: () => void
}) {
  const [reason, setReason] = useState<string>(reasonCodes[0])
  const [step, setStep] = useState<CreateStep>({ name: 'idle' })
  const sending = useRef(false)

  const blocked = step.name === 'sending' || (step.name === 'uncertain' && !step.checked)

  const submit = async () => {
    if (sending.current || blocked) return
    sending.current = true
    setStep({ name: 'sending' })
    try {
      const outcome: OpenOutcome = await openStandalone(staffCall, reason)
      switch (outcome.kind) {
        case 'opened':
          setStep({ name: 'idle' })
          onOpened(outcome.incident)
          break
        case 'denied':
          setStep({ name: 'denied', code: outcome.code })
          break
        case 'uncertain':
          setStep({ name: 'uncertain', why: outcome.why, checked: false, checking: false, checkFailed: false })
          break
        default:
          setStep({ name: 'refused', detail: outcome.kind === 'refused' ? outcome.detail : 'The request was refused.' })
      }
    } finally {
      sending.current = false
    }
  }

  const check = async () => {
    if (step.name !== 'uncertain' || step.checking) return
    setStep({ ...step, checking: true, checkFailed: false })
    const ok = await onCheckList()
    setStep({ ...step, checking: false, checked: ok, checkFailed: !ok })
  }

  return (
    <form
      className="form panel"
      aria-label="Open an incident"
      onSubmit={(e) => {
        e.preventDefault()
        void submit()
      }}
    >
      <h3>Open an incident</h3>
      <fieldset disabled={blocked}>
        <label>
          Reason
          <select value={reason} onChange={(e) => setReason(e.target.value)}>
            {reasonCodes.map((code) => (
              <option key={code} value={code}>
                {reasonName(code)}
              </option>
            ))}
          </select>
        </label>
      </fieldset>
      <p className="sub">To open one from a driver report, use that report: it can only ever start one incident.</p>
      <div className="actions">
        <button type="submit" className="primary" disabled={blocked}>
          {step.name === 'sending' ? 'Opening…' : 'Open incident'}
        </button>
      </div>

      {step.name === 'refused' && (
        <div className="notice" role="alert">
          <p>The incident was not opened: {step.detail}</p>
        </div>
      )}
      {step.name === 'denied' && (
        <div className="notice" role="alert">
          <p>
            {step.code === 401 ? 'Your session has ended. Nothing was opened.' : 'Your account is not allowed to open incidents, or no longer is. Nothing was opened.'}
          </p>
          <div className="actions">
            <button type="button" onClick={onRefreshMe}>
              Check my access again
            </button>
          </div>
        </div>
      )}
      {step.name === 'uncertain' && (
        <div className="notice warning" role="alert">
          <p>
            <strong>Not confirmed.</strong> No answer came back ({whyText[step.why]}), so the incident may or may not have
            been opened. It has not been sent again, because a second one would be a duplicate.
            {step.checked
              ? ' The list has been read again: look for it before opening another.'
              : ' Read the list again to see whether it was created, then open another only if it was not.'}
          </p>
          {!step.checked && (
            <div className="actions">
              <button type="button" disabled={step.checking} onClick={() => void check()}>
                {step.checking ? 'Reading the list…' : 'Read the open incidents'}
              </button>
            </div>
          )}
          {step.checkFailed && <p role="alert">The list could not be read either. Try again.</p>}
        </div>
      )}
    </form>
  )
}

type Detail = {
  incident: Incident | null
  status: 'read' | 'missing' | 'denied' | 'failed'
  code: 401 | 403 | null
  /** Read on this page earlier, so a later 404 means it has gone, not that the address is wrong. */
  shownBefore: boolean
}

type ResolveStep = { name: 'idle' } | { name: 'sending' } | { name: 'done'; outcome: ResolveOutcome }

function IncidentDetail({
  incidentId,
  me,
  canManage,
  inList,
  listLoaded,
  onIncident,
  onRefreshMe,
}: {
  incidentId: string
  me: Me
  canManage: boolean
  inList: boolean
  listLoaded: boolean
  onIncident: (incident: Incident) => void
  onRefreshMe: () => void
}) {
  const valid = isGuid(incidentId)
  const [step, setStep] = useState<ResolveStep>({ name: 'idle' })
  const sending = useRef(false)
  const headingRef = useRef<HTMLHeadingElement>(null)

  const { data, refresh } = usePolled<Detail>(`incident:${valid ? incidentId : 'invalid'}`, async (signal, previous) => {
    if (!valid) return { incident: null, status: 'missing', code: null, shownBefore: false }
    const read = await readIncidentOnce(staffCall, incidentId)
    if (signal.aborted) return previous ?? { incident: null, status: 'failed', code: null, shownBefore: false }
    const earlier = previous?.incident ?? null
    if (read.status === 'read') {
      onIncident(read.incident)
      return { incident: read.incident, status: 'read', code: null, shownBefore: true }
    }
    return {
      incident: read.status === 'missing' ? null : earlier,
      status: read.status,
      code: read.status === 'denied' ? read.code : null,
      shownBefore: previous?.shownBefore ?? false,
    }
  })

  const outcome = step.name === 'done' ? step.outcome : null
  const polled = data?.incident ?? null
  // Resolution is recorded once and never changes, so any copy that is resolved is right.
  const shown: Incident | null =
    polled && isResolved(polled)
      ? polled
      : outcome?.kind === 'resolved'
        ? outcome.incident
        : outcome?.kind === 'uncertain' && outcome.check.status === 'read' && (polled === null || isResolved(outcome.check.incident))
          ? outcome.check.incident
          : polled

  // Move focus to the incident when it opens, so the choice made in the list is announced.
  const ready = shown !== null
  useEffect(() => {
    if (ready) headingRef.current?.focus()
  }, [ready, incidentId])

  const send = async () => {
    if (sending.current || shown === null) return
    sending.current = true
    setStep({ name: 'sending' })
    try {
      const result = await resolve(staffCall, incidentId)
      setStep({ name: 'done', outcome: result })
      if (result.kind === 'resolved') {
        onIncident(result.incident)
        refresh()
      }
      if (result.kind === 'uncertain' && result.check.status === 'read') onIncident(result.check.incident)
    } finally {
      sending.current = false
    }
  }

  if (!valid || data?.status === 'missing') {
    return (
      <div className="notice" role="alert">
        <p>
          {data?.shownBefore
            ? 'This incident is no longer available. It was shown earlier on this page but the server no longer has it.'
            : 'No incident has this ID.'}
        </p>
        <AppLink to="/incidents">Back to incidents</AppLink>
      </div>
    )
  }

  const code = data?.status === 'denied' ? data.code : outcome?.kind === 'denied' ? outcome.code : null
  if (code !== null) {
    return (
      <div className="notice" role="alert">
        <p>
          {code === 401
            ? 'Your session has ended. Nothing was changed.'
            : 'Your account is not allowed to read or change incidents, or no longer is. Nothing was changed.'}
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
        Loading the incident…
      </p>
    ) : (
      <div className="notice" role="alert">
        <p>The incident could not be read. This is a connection or server problem, not a permission problem.</p>
        <div className="actions">
          <button type="button" onClick={refresh}>
            Try again
          </button>
        </div>
      </div>
    )
  }

  const resolved = isResolved(shown)
  const uncertain = outcome?.kind === 'uncertain' ? outcome : null
  const reportTo = shown.sourceReportId ? `/reports/${shown.sourceReportId}` : null
  const historyTo = `/audit/incidents/${shown.incidentId}`

  return (
    <article className="panel" aria-labelledby="incident-heading">
      <h2 id="incident-heading" tabIndex={-1} ref={headingRef}>
        {reasonName(shown.reasonCode)}
      </h2>

      {data?.status === 'failed' && (
        <div className="notice warning" role="status">
          <p>This incident could not be refreshed, so what is shown may be out of date.</p>
          <div className="actions">
            <button type="button" onClick={refresh}>
              Try again
            </button>
          </div>
        </div>
      )}
      {listLoaded && !inList && <p className="sub">This incident is not in the list as it is filtered now. It is still open here.</p>}

      <dl className="facts">
        <div>
          <dt>State</dt>
          <dd>
            <span className={`sr-badge ${resolved ? 'sr-tone-neutral' : 'sr-tone-warning'}`}>{resolved ? 'Resolved' : 'Open'}</span>
          </dd>
        </div>
        <div>
          <dt>Opened by</dt>
          <dd>{personName(shown.openedByName)}</dd>
        </div>
        <div>
          <dt>Opened at</dt>
          <dd>{dateTimeText(shown.openedAt)}</dd>
        </div>
        {resolved && shown.resolvedAt !== null && (
          <div>
            <dt>Resolved at</dt>
            <dd>{dateTimeText(shown.resolvedAt)}</dd>
          </div>
        )}
      </dl>

      <p>
        {shown.sourceReportId === null ? (
          'Opened on its own, not from a driver report.'
        ) : reportTo !== null && canOpenPath(me, reportTo) ? (
          <>
            Opened from <AppLink to={reportTo}>a driver report</AppLink>.
          </>
        ) : (
          'Opened from a driver report.'
        )}
      </p>
      <p className="muted">
        {shown.changeIds.length === 0
          ? 'No published change names this incident.'
          : `${shown.changeIds.length} published change${shown.changeIds.length === 1 ? '' : 's'} name this incident. Resolving it does not withdraw them.`}
      </p>
      {canOpenPath(me, historyTo) && <AppLink to={historyTo}>View this incident’s history</AppLink>}

      <section aria-labelledby="resolve-heading" className="stack tight">
        <h3 id="resolve-heading">Resolution</h3>
        {resolved ? (
          <div className="notice success" role="status">
            <p>
              Resolved{shown.resolvedAt !== null ? ` at ${dateTimeText(shown.resolvedAt)}` : ''}. This is the first resolution
              recorded by SmartRail. The report’s acknowledgement and every published change are as they were.
            </p>
          </div>
        ) : (
          <div className="notice neutral">
            <p>This incident is open.</p>
          </div>
        )}

        {outcome?.kind === 'refused' && (
          <div className="notice" role="alert">
            <p>The incident was not resolved: {outcome.detail}</p>
          </div>
        )}
        {uncertain && !resolved && (
          <div className="notice warning" role="alert">
            <p>
              <strong>Not confirmed.</strong> No answer came back ({whyText[uncertain.why]}).{' '}
              {uncertain.check.status === 'read'
                ? 'SmartRail was then asked directly and still shows the incident as open. You can resolve it again.'
                : 'SmartRail could not tell whether it was recorded, and was not asked again. Check again before resolving.'}
            </p>
          </div>
        )}

        {canManage && !resolved && (
          <div className="actions">
            {uncertain && uncertain.check.status !== 'read' ? (
              <button
                type="button"
                className="primary"
                onClick={async () => {
                  const check = await readIncidentOnce(staffCall, incidentId)
                  setStep({ name: 'done', outcome: { ...uncertain, check } })
                  if (check.status === 'read') onIncident(check.incident)
                }}
              >
                Check what SmartRail recorded
              </button>
            ) : (
              <button type="button" className="primary" disabled={step.name === 'sending'} onClick={() => void send()}>
                {step.name === 'sending' ? 'Resolving…' : 'Resolve incident'}
              </button>
            )}
          </div>
        )}
        {!canManage && <p className="sub">Your account can read incidents but not change them.</p>}
        <p className="sub">Resolving does not acknowledge a report or withdraw a published change.</p>
      </section>
    </article>
  )
}
