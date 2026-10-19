import { useReducer, useRef, useState, type MouseEvent } from 'react'
import type { Me } from '../auth'
import { navigate } from '../router.ts'
import { usePolled } from '../workspace/hooks.ts'
import { staffCall } from '../workspace/live.ts'
import { clockText } from '../workspace/time.ts'
import { isAcknowledged } from './acknowledge.ts'
import { emptyInbox, filterKey, inboxReducer, visibleReports } from './inbox.ts'
import { journeyLabel, type JourneyChoice } from './journeyChoices.ts'
import { dateTimeText, personName } from './format.ts'
import { reasonName } from './reasons.ts'
import { defaultFilter, listReports, type AckFilter, type InboxFilter } from './reportsApi.ts'
import { ReportDetail } from './ReportDetail.tsx'
import type { ControlReport } from './types.ts'
import { useJourneyChoices } from './useJourneyChoices.ts'
import './reports.css'

type Status =
  | { kind: 'ok'; readAt: number }
  | { kind: 'failed'; why: 'network' | 'server'; lastOk: number | null }
  | { kind: 'denied'; code: 401 | 403 }

const ackLabels: Record<AckFilter, string> = {
  no: 'Waiting for acknowledgement',
  yes: 'Acknowledged',
  all: 'All reports',
}

/**
 * The paginated Control inbox. It shows the newest reports first, refreshes only the first page
 * every 15 seconds while the page is visible, and reads more only when asked. Reading or
 * selecting a report here never acknowledges it.
 */
export function ReportInbox({ me, selectedId, onRefreshMe }: { me: Me; selectedId: string | null; onRefreshMe: () => void }) {
  const [filter, setFilter] = useState<InboxFilter>(defaultFilter)
  const [inbox, dispatch] = useReducer(inboxReducer, defaultFilter, emptyInbox)
  const { choices } = useJourneyChoices()
  const loadingMore = useRef(false)
  const key = filterKey(filter)

  const { data: status, refresh } = usePolled<Status>(`inbox:${key}`, async (signal, previous) => {
    const result = await listReports(staffCall, filter, null)
    if (signal.aborted) return previous ?? { kind: 'failed', why: 'network', lastOk: null }
    if (result.kind === 'ok') {
      dispatch({ type: 'firstPage', key, page: result.body })
      return { kind: 'ok', readAt: Date.now() }
    }
    if (result.kind === 'denied') return { kind: 'denied', code: result.status }
    const lastOk = previous?.kind === 'ok' ? previous.readAt : previous?.kind === 'failed' ? previous.lastOk : null
    return { kind: 'failed', why: result.kind === 'no-answer' ? 'network' : 'server', lastOk }
  })

  const changeFilter = (next: InboxFilter) => {
    setFilter(next)
    dispatch({ type: 'key', key: filterKey(next) })
  }

  const loadMore = async () => {
    const from = inbox.tail
    if (from === null || loadingMore.current) return
    loadingMore.current = true
    dispatch({ type: 'moreStarted', key, from })
    try {
      const result = await listReports(staffCall, filter, from)
      dispatch(result.kind === 'ok' ? { type: 'morePage', key, from, page: result.body } : { type: 'moreFailed', key, from })
    } finally {
      loadingMore.current = false
    }
  }

  const reports = visibleReports(inbox)
  const selectedInList = selectedId !== null && reports.some((r) => r.reportId === selectedId)

  return (
    <div className="reports-layout">
      <section className="stack tight" aria-labelledby="inbox-heading">
        <h2 id="inbox-heading">Inbox</h2>
        <p className="muted">
          Newest first, refreshed every 15 seconds while this page is open. Reading a report does not acknowledge it.
        </p>
        <InboxFilters filter={filter} choices={choices} onChange={changeFilter} />

        {status?.kind === 'denied' ? (
          <div className="notice" role="alert">
            <p>
              {status.code === 401
                ? 'Your session has ended, so the inbox cannot be read.'
                : 'Your account is not allowed to read reports, or no longer is.'}
            </p>
            <div className="actions">
              <button type="button" onClick={onRefreshMe}>
                Check my access again
              </button>
            </div>
          </div>
        ) : !inbox.loaded && (status === null || status.kind === 'ok') ? (
          <p role="status" className="muted">
            Loading reports…
          </p>
        ) : !inbox.loaded ? (
          <div className="notice" role="alert">
            <p>The inbox could not be read. This is a connection or server problem, not a permission problem.</p>
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
                  The inbox could not be refreshed
                  {status.lastOk !== null ? `; this is the list as of ${clockText(status.lastOk)}` : ''}. New reports may be
                  missing.
                </p>
                <div className="actions">
                  <button type="button" onClick={refresh}>
                    Try again
                  </button>
                </div>
              </div>
            )}
            {reports.length === 0 ? (
              <div className="notice neutral" role="status">
                <p>
                  {filter.acknowledged === 'no' ? 'Nothing is waiting for acknowledgement.' : 'No reports match this filter.'}
                </p>
              </div>
            ) : (
              <ul className="plain report-list" aria-label="Reports">
                {reports.map((report) => (
                  <ReportRow
                    key={report.reportId}
                    report={report}
                    choices={choices}
                    selected={report.reportId === selectedId}
                  />
                ))}
              </ul>
            )}
            {inbox.extended && (
              <p className="sub">
                Reports below the newest page were read earlier and may be out of date.{' '}
                <button type="button" onClick={() => dispatch({ type: 'reload' })}>
                  Reload the list
                </button>
              </p>
            )}
            {inbox.tail !== null ? (
              <div className="actions">
                <button type="button" disabled={inbox.loadingFrom !== null} onClick={() => void loadMore()}>
                  {inbox.loadingFrom !== null ? 'Loading…' : 'Load more reports'}
                </button>
                {inbox.loadMoreFailed && (
                  <span role="alert" className="problem">
                    More reports could not be read. Try again.
                  </span>
                )}
              </div>
            ) : (
              reports.length > 0 && <p className="sub">That is every report for this filter.</p>
            )}
          </>
        )}
      </section>

      <section aria-label="Report detail" className="stack tight">
        {selectedId === null ? (
          <div className="notice neutral">
            <p>Choose a report to read it.</p>
          </div>
        ) : (
          <ReportDetail
            key={selectedId}
            reportId={selectedId}
            me={me}
            choices={choices}
            inList={selectedInList}
            listLoaded={inbox.loaded}
            onReport={(report) => dispatch({ type: 'patch', item: report })}
            onRefreshMe={onRefreshMe}
          />
        )}
      </section>
    </div>
  )
}

function InboxFilters({
  filter,
  choices,
  onChange,
}: {
  filter: InboxFilter
  choices: readonly JourneyChoice[]
  onChange: (filter: InboxFilter) => void
}) {
  const known = filter.journeyId === null || choices.some((c) => c.journeyId === filter.journeyId)
  return (
    <form className="filters" aria-label="Filter reports" onSubmit={(e) => e.preventDefault()}>
      <label>
        Show
        <select value={filter.acknowledged} onChange={(e) => onChange({ ...filter, acknowledged: e.target.value as AckFilter })}>
          {(Object.keys(ackLabels) as AckFilter[]).map((value) => (
            <option key={value} value={value}>
              {ackLabels[value]}
            </option>
          ))}
        </select>
      </label>
      <label>
        Journey
        <select value={filter.journeyId ?? ''} onChange={(e) => onChange({ ...filter, journeyId: e.target.value === '' ? null : e.target.value })}>
          <option value="">All journeys</option>
          {!known && filter.journeyId !== null && <option value={filter.journeyId}>{journeyLabel(choices, filter.journeyId)}</option>}
          {choices.map((choice) => (
            <option key={choice.journeyId} value={choice.journeyId}>
              {choice.label}
            </option>
          ))}
        </select>
      </label>
    </form>
  )
}

function ReportRow({ report, choices, selected }: { report: ControlReport; choices: readonly JourneyChoice[]; selected: boolean }) {
  const to = `/reports/${report.reportId}`
  const open = (event: MouseEvent<HTMLAnchorElement>) => {
    if (event.metaKey || event.ctrlKey || event.shiftKey || event.button !== 0) return
    event.preventDefault()
    navigate(to)
  }
  const acknowledged = isAcknowledged(report)
  return (
    <li>
      <a href={to} onClick={open} className={`report-row${selected ? ' selected' : ''}`} aria-current={selected ? 'true' : undefined}>
        <span className="row-title">
          <strong>{reasonName(report.reasonCode)}</strong>
          <span className={`sr-badge ${acknowledged ? 'sr-tone-success' : 'sr-tone-warning'}`}>
            {acknowledged ? 'Acknowledged' : 'Waiting'}
          </span>
        </span>
        <span className="sub">{journeyLabel(choices, report.journeyId)}</span>
        <span className="sub">
          From {personName(report.submittedByName)}. Received {dateTimeText(report.receivedAt)}
        </span>
        {acknowledged && report.acknowledgedAt !== null && (
          <span className="sub">
            Acknowledged by {personName(report.acknowledgedByName)} at {dateTimeText(report.acknowledgedAt)}
          </span>
        )}
      </a>
    </li>
  )
}
