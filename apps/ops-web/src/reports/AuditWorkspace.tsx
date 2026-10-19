import { useEffect, useReducer, useRef, useState } from 'react'
import { canOpenPath } from '../access.ts'
import type { Me } from '../auth'
import { navigate } from '../router.ts'
import { staffCall } from '../workspace/live.ts'
import { AppLink } from '../workspace/parts.tsx'
import { clockText } from '../workspace/time.ts'
import { actionText, outcomeText, targetPath, targetTypeText } from './auditText.ts'
import { loadAuditPage, type AuditLoad } from './auditPages.ts'
import { dateTimeText, isGuid } from './format.ts'
import { journeyLabel } from './journeyChoices.ts'
import { emptyPaged, pagedReducer, visibleItems } from './paged.ts'
import type { AuditSubject } from './reportsApi.ts'
import type { AuditEntry } from './types.ts'
import { useJourneyChoices } from './useJourneyChoices.ts'
import './reports.css'

const auditPath = /^\/audit\/(journeys|incidents)\/([^/]+)$/
const auditReducer = pagedReducer<AuditEntry>((e) => e.id)
const entryId = (e: AuditEntry) => e.id

/**
 * The Auditor's read-only history of one journey or incident. Nothing here writes: no
 * acknowledgement, incident or change control exists on these pages. The shell has checked
 * audit.read; the API checks it again on every page read.
 */
export function AuditWorkspace({ me, path, onRefreshMe }: { me: Me; path: string; onRefreshMe: () => void }) {
  const match = auditPath.exec(path)
  if (path === '/audit') return <AuditLanding />
  if (!match || !isGuid(match[2])) {
    return (
      <p className="notice" role="alert">
        There is no page at this address.
      </p>
    )
  }
  const subject: AuditSubject = { type: match[1] === 'journeys' ? 'journey' : 'incident', id: match[2] }
  return <AuditHistory key={`${me.accountId}:${subject.type}:${subject.id}`} me={me} subject={subject} onRefreshMe={onRefreshMe} />
}

function AuditLanding() {
  const { choices, failed } = useJourneyChoices()
  const [journeyId, setJourneyId] = useState('')
  return (
    <section className="stack tight" aria-label="History picker">
      <p className="muted">
        This is a read-only record of who did what and when. Choose a journey, or open an incident and follow its history link.
      </p>
      <form
        className="filters"
        aria-label="History picker"
        onSubmit={(e) => {
          e.preventDefault()
          if (journeyId !== '') navigate(`/audit/journeys/${journeyId}`)
        }}
      >
        <label>
          Journey
          <select value={journeyId} onChange={(e) => setJourneyId(e.target.value)}>
            <option value="">Choose a journey</option>
            {choices.map((choice) => (
              <option key={choice.journeyId} value={choice.journeyId}>
                {choice.label}
              </option>
            ))}
          </select>
        </label>
        <div className="actions">
          <button type="submit" className="primary" disabled={journeyId === ''}>
            View history
          </button>
        </div>
      </form>
      {failed && (
        <p className="sub problem" role="status">
          Some journeys could not be listed. Open a journey from the corridor and follow its history link instead.
        </p>
      )}
      <p>
        <AppLink to="/incidents">Incidents</AppLink> lists every incident; each one links to its history.
      </p>
    </section>
  )
}

type Status = 'loading' | 'ok' | 'denied-401' | 'denied-403' | 'missing' | 'failed' | 'refused'

function AuditHistory({ me, subject, onRefreshMe }: { me: Me; subject: AuditSubject; onRefreshMe: () => void }) {
  const key = `${subject.type}:${subject.id}`
  const [list, dispatch] = useReducer(auditReducer, key, (k) => emptyPaged<AuditEntry>(k))
  const [status, setStatus] = useState<Status>('loading')
  const [readAt, setReadAt] = useState<number | null>(null)
  const [reads, setReads] = useState(0)
  const loadingMore = useRef(false)
  const { choices } = useJourneyChoices()

  // The first page is read when the page opens and again only when the person asks.
  useEffect(() => {
    let cancelled = false
    void loadAuditPage(staffCall, subject, null).then((result: AuditLoad) => {
      if (cancelled) return
      if (result.kind === 'ok') {
        dispatch({ type: 'firstPage', key, page: result.page })
        setReadAt(Date.now())
        setStatus('ok')
      } else {
        setStatus(result.kind === 'denied' ? (result.code === 401 ? 'denied-401' : 'denied-403') : result.kind)
      }
    })
    return () => {
      cancelled = true
    }
  }, [subject, key, reads])

  const reload = () => {
    dispatch({ type: 'reload' })
    setStatus('loading')
    setReads((n) => n + 1)
  }

  const loadMore = async () => {
    const from = list.tail
    if (from === null || loadingMore.current) return
    loadingMore.current = true
    dispatch({ type: 'moreStarted', key, from })
    try {
      const result = await loadAuditPage(staffCall, subject, from)
      dispatch(result.kind === 'ok' ? { type: 'morePage', key, from, page: result.page } : { type: 'moreFailed', key, from })
    } finally {
      loadingMore.current = false
    }
  }

  const title =
    subject.type === 'journey' ? `History of ${journeyLabel(choices, subject.id)}` : `History of incident ${subject.id.slice(0, 8)}`
  const subjectPath = subject.type === 'journey' ? `/corridor/journeys/${subject.id}` : `/incidents/${subject.id}`
  const entries = visibleItems(list, entryId)

  const back = (
    <p>
      <AppLink to="/audit">← Audit history</AppLink>
      {canOpenPath(me, subjectPath) && (
        <>
          {' · '}
          <AppLink to={subjectPath}>{subject.type === 'journey' ? 'Open the journey' : 'Open the incident'}</AppLink>
        </>
      )}
    </p>
  )

  if (status === 'denied-401' || status === 'denied-403') {
    return (
      <section className="stack tight">
        {back}
        <div className="notice" role="alert">
          <p>
            {status === 'denied-401'
              ? 'Your session has ended, so the history cannot be read.'
              : 'Your account is not allowed to read audit history, or no longer is.'}
          </p>
          <div className="actions">
            <button type="button" onClick={onRefreshMe}>
              Check my access again
            </button>
          </div>
        </div>
      </section>
    )
  }
  if (status === 'missing') {
    return (
      <section className="stack tight">
        {back}
        <div className="notice" role="alert">
          <p>{subject.type === 'journey' ? 'No journey has this ID.' : 'No incident has this ID.'}</p>
        </div>
      </section>
    )
  }

  return (
    <section className="stack tight" aria-labelledby="audit-heading">
      {back}
      <h2 id="audit-heading">{title}</h2>
      <p className="muted">
        Oldest first. Times are Sri Lanka time. Request contents, tokens and secrets are never recorded here. This page
        cannot change anything.
      </p>

      {status === 'loading' && (
        <p role="status" className="muted">
          Loading the history…
        </p>
      )}
      {(status === 'failed' || status === 'refused') && (
        <div className="notice" role="alert">
          <p>The history could not be read. This is a connection or server problem, not a permission problem.</p>
          <div className="actions">
            <button type="button" onClick={reload}>
              Try again
            </button>
          </div>
        </div>
      )}

      {status === 'ok' && (
        <>
          {entries.length === 0 ? (
            <div className="notice neutral" role="status">
              <p>Nothing has been recorded yet.</p>
            </div>
          ) : (
            <div className="table-wrap" role="region" aria-label="Audit entries" tabIndex={0}>
              <table>
                <caption className="sr-only">Audit entries, oldest first</caption>
                <thead>
                  <tr>
                    <th scope="col">Time</th>
                    <th scope="col">Who</th>
                    <th scope="col">Action</th>
                    <th scope="col">Outcome</th>
                    <th scope="col">About</th>
                    <th scope="col">Correlation ID</th>
                  </tr>
                </thead>
                <tbody>
                  {entries.map((entry) => (
                    <AuditRow key={entry.id} entry={entry} me={me} />
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {list.tail === null ? (
            <p className="sub" role="status">
              End of history: {entries.length} {entries.length === 1 ? 'entry' : 'entries'}, read at {readAt !== null ? clockText(readAt) : 'an unknown time'}.
              Later actions will not appear until you check again.
            </p>
          ) : (
            <p className="sub" role="status">
              Showing the first {entries.length} entries. This is not the whole history: more entries follow.
            </p>
          )}
          <div className="actions">
            {list.tail !== null && (
              <button type="button" className="primary" disabled={list.loadingFrom !== null} onClick={() => void loadMore()}>
                {list.loadingFrom !== null ? 'Loading…' : 'Load later entries'}
              </button>
            )}
            <button type="button" onClick={reload}>
              Check from the start
            </button>
            {list.loadMoreFailed && (
              <span role="alert" className="problem">
                Later entries could not be read. Try again.
              </span>
            )}
          </div>
        </>
      )}
    </section>
  )
}

function AuditRow({ entry, me }: { entry: AuditEntry; me: Me }) {
  const to = targetPath(entry)
  // A link appears only where this account may open the target, so history never leads to a screen it cannot read.
  const linkable = to !== null && canOpenPath(me, to)
  const denied = entry.outcome === 'DENIED'
  return (
    <tr>
      <td>
        <time dateTime={entry.occurredAt} title={`${entry.occurredAt} (UTC)`}>
          {dateTimeText(entry.occurredAt)}
        </time>
      </td>
      <td>{entry.actorName}</td>
      <td>
        {actionText(entry.action)}
        <span className="sub">{entry.action}</span>
      </td>
      <td>
        <span className={`sr-badge ${denied ? 'sr-tone-danger' : 'sr-tone-success'}`}>{outcomeText(entry.outcome)}</span>
      </td>
      <td>
        {linkable && to !== null ? <AppLink to={to}>{targetTypeText(entry.targetType)}</AppLink> : targetTypeText(entry.targetType)}
        {entry.targetId !== null && <span className="sub">{entry.targetId.slice(0, 8)}</span>}
      </td>
      <td>
        <code>{entry.correlationId}</code>
      </td>
    </tr>
  )
}
