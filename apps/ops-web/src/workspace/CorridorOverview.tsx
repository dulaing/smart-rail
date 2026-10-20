import { useMemo, useState } from 'react'
import type { Me } from '../auth'
import { colomboDate, clockText, timeOrDash } from './time.ts'
import { correctedNow, loadCorridor, type CorridorRow, type CorridorSnapshot } from './corridorLoader.ts'
import { confirmedFacts, delayOf, evidenceOf, failureText, hasConfirmedChange, lastKnownStation, stationName } from './facts.ts'
import { usePolled, useNow } from './hooks.ts'
import { publicReader } from './live.ts'
import { corridorStations } from './rights.ts'
import { AppLink, DelayCell } from './parts.tsx'
import './workspace.css'

type ServiceFilter = 'ALL' | 'DELAYED' | 'CANCELLED' | 'ON_TIME' | 'UNKNOWN'

/**
 * The corridor overview (/corridor) and the list of journeys with confirmed changes (/changes).
 * Both read the same bounded set of dated journeys; the second keeps the ones with a change in force.
 */
export function CorridorOverview({ me, mode }: { me: Me; mode: 'all' | 'changed' }) {
  const stationIds = useMemo(() => corridorStations(me), [me])
  const scopeKey = stationIds === null ? 'all' : [...stationIds].sort().join(',')

  const { data, refresh } = usePolled<CorridorSnapshot>(
    `corridor:${scopeKey}`,
    (signal, previous) =>
      loadCorridor({
        reader: publicReader,
        now: correctedNow(previous?.clockOffsetMs ?? null, Date.now()),
        previous,
        stationIds,
        signal,
      }),
    // The service date rolled over while this refresh ran: its answer is for yesterday's list.
    (snapshot) => snapshot.today !== colomboDate(correctedNow(snapshot.clockOffsetMs, Date.now())),
  )
  const now = useNow(data?.clockOffsetMs ?? null)
  const [serviceDate, setServiceDate] = useState('ALL')
  const [stationId, setStationId] = useState('ALL')
  const [serviceFilter, setServiceFilter] = useState<ServiceFilter>('ALL')
  const accessibleStations = useMemo(() => {
    const ids = corridorStations(me)
    return (data?.stations ?? []).filter((station) => ids === null || ids.has(station.id))
  }, [data?.stations, me])

  if (data === null) {
    return (
      <p role="status" className="muted">
        Loading trains…
      </p>
    )
  }

  if (!data.hasTimetable) {
    return (
      <section className="notice" aria-label="Trains unavailable">
        <p role="alert">
          The timetable could not be read, so no trains can be listed. {failureText(data.lastFailure)} This is not an
          empty corridor.
        </p>
        <div className="actions">
          <button type="button" onClick={refresh}>
            Try again
          </button>
        </div>
      </section>
    )
  }

  const allRows =
    mode === 'all'
      ? data.rows
      : data.rows.filter((row) => row.running !== null && hasConfirmedChange(confirmedFacts(row.running, data.stations)))
  const dates = [...new Set(allRows.map((row) => row.journey.serviceDate))].sort().reverse()
  const rows = allRows.filter((row) => {
    if (serviceDate !== 'ALL' && row.journey.serviceDate !== serviceDate) return false
    if (stationId !== 'ALL' && !row.journey.stops.some((stop) => stop.stationId === stationId)) return false
    return serviceFilter === 'ALL' || serviceStatus(row.running, evidenceOf(row.running, now), data.stations) === serviceFilter
  })
  const delayedCount = allRows.filter((row) => row.running && hasDelay(row.running)).length
  const ageingCount = allRows.filter((row) => evidenceOf(row.running, now) === 'AGEING').length
  const cancelledCount = allRows.filter((row) => row.running?.availability === 'CANCELLED').length
  const focusStation = accessibleStations.find((station) => station.code === 'GPH') ?? accessibleStations[0]
  const lost = data.status === 'lost'

  return (
    <section className="stack corridor-page" aria-label={mode === 'all' ? 'Corridor trains' : 'Trains with confirmed changes'}>
      <div className="corridor-subtitle">
        <p className="muted">{dateLabel(data.today)} · inbound services · refreshed {data.readAt === null ? '—' : refreshTime(data.readAt)}</p>
      </div>

      <div className="corridor-summary" aria-label="Service summary">
        <SummaryCard label="Services shown" value={allRows.length} />
        <SummaryCard label="Delayed" value={delayedCount} tone="warning" />
        <SummaryCard label="Observation ageing" value={ageingCount} tone="warning" />
        <SummaryCard label="Cancelled" value={cancelledCount} tone="danger" />
      </div>

      {lost && (
        <div className="notice" role="alert">
          <p>
            <strong>Connection lost.</strong> Nothing below is current
            {data.readAt !== null ? `: it was last read at ${clockText(data.readAt)}` : ''}. Evidence keeps ageing, and
            this page keeps trying.
          </p>
          <div className="actions">
            <button type="button" onClick={refresh}>
              Try now
            </button>
          </div>
        </div>
      )}
      {data.status === 'partial' && (
        <p className="notice warning" role="status">
          {data.failedReads} {data.failedReads === 1 ? 'read' : 'reads'} failed in the latest refresh. {failureText(data.lastFailure)} Rows
          marked “Could not refresh” show their earlier reading.
        </p>
      )}

      <div className="corridor-filters">
        <label>
          Service date
          <select value={serviceDate} onChange={(event) => setServiceDate(event.target.value)}>
            <option value="ALL">All service dates</option>
            {dates.map((date) => <option value={date} key={date}>{dateLabel(date, true)}</option>)}
          </select>
        </label>
        <label>
          Station
          <select value={stationId} onChange={(event) => setStationId(event.target.value)}>
            <option value="ALL">Pilot corridor</option>
            {accessibleStations.map((station) => <option value={station.id} key={station.id}>{station.nameEn}</option>)}
          </select>
        </label>
        <label>
          Service state
          <select value={serviceFilter} onChange={(event) => setServiceFilter(event.target.value as ServiceFilter)}>
            <option value="ALL">All services</option>
            <option value="DELAYED">Delayed</option>
            <option value="CANCELLED">Cancelled</option>
            <option value="ON_TIME">On time</option>
            <option value="UNKNOWN">Unknown</option>
          </select>
        </label>
        <button type="button" className="corridor-refresh" onClick={refresh}>Refresh</button>
      </div>

      {rows.length === 0 ? (
        <p className="notice neutral" role="status">
          {mode === 'all'
            ? 'No trains are scheduled on these service dates for your access. This is a successful, empty answer.'
            : 'No confirmed changes are in force on the trains listed here.'}
        </p>
      ) : (
        <div id="corridor-train-list" className="table-wrap" role="region" aria-label="Train list" tabIndex={0}>
          <table>
            <thead>
              <tr>
                <th scope="col">Train</th>
                <th scope="col">Last observation</th>
                <th scope="col">Freshness</th>
                <th scope="col">Delay</th>
                <th scope="col">Service</th>
                <th scope="col">{focusStation?.nameEn ?? 'Station'}</th>
              </tr>
            </thead>
            <tbody>
                {rows.map((row) => <TrainRow key={row.journey.journeyId} row={row} snapshot={data} now={now} offline={lost} focusStationId={focusStation?.id ?? null} />)}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}

function TrainRow({ row, snapshot, now, offline, focusStationId }: { row: CorridorRow; snapshot: CorridorSnapshot; now: number; offline: boolean; focusStationId: string | null }) {
  const { journey, running } = row
  const first = journey.stops[0]
  const last = journey.stops[journey.stops.length - 1]
  const evidence = evidenceOf(running, now)

  const changed = running !== null && hasConfirmedChange(confirmedFacts(running, snapshot.stations))
  return (
    <tr className={changed ? 'corridor-row-selected' : undefined}>
      <th scope="row">
        <AppLink to={`/corridor/journeys/${journey.journeyId}`}>{journey.trainNumber}</AppLink>
        <span className="sub">
          to {stationName(snapshot.stations, last?.stationId)}
        </span>
      </th>
      <td>
        {first && last ? (
          <>
            {timeOrDash(first.scheduledDeparture ?? first.scheduledArrival)} {stationName(snapshot.stations, first.stationId)}
            <span className="sub">
              to {timeOrDash(last.scheduledArrival ?? last.scheduledDeparture)} {stationName(snapshot.stations, last.stationId)}
            </span>
          </>
        ) : (
          <span className="muted">No stops</span>
        )}
      </td>
      {running === null ? (
        <td colSpan={4}>
          <span className="problem" role="status">
            {row.readFailed ? 'Could not read running information for this train.' : 'No running information yet.'}
          </span>
        </td>
      ) : (
        <>
          <td>
            {lastKnownStation(running, snapshot.stations) ?? <span className="muted">No observation yet</span>}
            <span className="sub">
              {running.lastObservation ? clockText(running.lastObservation.observedAt) : '—'}
            </span>
            {row.readFailed && (
              <span className="sub problem" role="status">
                Could not refresh: showing the earlier reading.
              </span>
            )}
          </td>
          <td><FreshnessSummary running={running} now={now} offline={offline} /></td>
          <td>
            <DelayCell delay={delayOf(running, evidence)} evidence={evidence} />
          </td>
          <td>{serviceText(running, evidence)}</td>
          <td>{platformText(running, focusStationId)}</td>
        </>
      )}
    </tr>
  )
}

function SummaryCard({ label, value, tone = 'neutral' }: { label: string; value: number; tone?: 'neutral' | 'warning' | 'danger' }) {
  return <div className={`summary-card summary-${tone}`}><span>{label}</span><strong>{value}</strong></div>
}

function serviceStatus(running: CorridorRow['running'], evidence: ReturnType<typeof evidenceOf>, stations: CorridorSnapshot['stations']): ServiceFilter {
  if (running === null || evidence === 'UNKNOWN') return 'UNKNOWN'
  const facts = confirmedFacts(running, stations)
  if (facts.availability === 'CANCELLED') return 'CANCELLED'
  return hasDelay(running) ? 'DELAYED' : 'ON_TIME'
}

function hasDelay(running: NonNullable<CorridorRow['running']>): boolean {
  return running.confirmedDelayMinutes !== null
    ? running.confirmedDelayMinutes > 0
    : running.availability === 'AS_SCHEDULED' && (running.delaySeconds ?? 0) >= 60
}

function serviceText(running: NonNullable<CorridorRow['running']>, evidence: ReturnType<typeof evidenceOf>): string {
  if (running.availability === 'CANCELLED') return 'Cancelled'
  if (running.availability === 'ENDS_EARLY') return 'Ends early'
  if (evidence === 'UNKNOWN') return 'Unknown'
  return hasDelay(running) ? 'Delayed' : 'On time'
}

function platformText(running: NonNullable<CorridorRow['running']>, focusStationId: string | null) {
  if (!focusStationId) return '—'
  const stop = running.stops.find((item) => item.stationId === focusStationId)
  if (!stop) return '—'
  if (stop.confirmedPlatform !== null) {
    return <>{`Platform ${stop.confirmedPlatform}`}<span className="sub">{stop.confirmedPlatformAt ? `confirmed ${clockText(stop.confirmedPlatformAt)}` : 'Confirmed'}</span></>
  }
  return stop.plannedPlatform === null ? '—' : `Planned platform ${stop.plannedPlatform}`
}

function FreshnessSummary({ running, now, offline }: { running: NonNullable<CorridorRow['running']>; now: number; offline: boolean }) {
  const observedAt = running.lastObservation?.observedAt ?? null
  const evidence = evidenceOf(running, now)
  if (observedAt === null) return <span className="muted">Not available<span className="sub">No update yet</span></span>
  const ageMinutes = Math.max(1, Math.floor((now - Date.parse(observedAt)) / 60000))
  const label = offline ? 'Offline' : evidence === 'CURRENT' ? 'Current' : evidence === 'AGEING' ? 'Ageing' : 'Position unknown'
  const detail = offline ? `last update ${clockText(observedAt)}` : evidence === 'UNKNOWN' ? `last update ${clockText(observedAt)}` : `${ageMinutes} min old`
  return <span>{label}<span className="sub">{detail}</span></span>
}

function dateLabel(date: string, includeYear = false): string {
  return new Intl.DateTimeFormat('en-LK', { weekday: 'short', day: 'numeric', month: 'short', ...(includeYear ? { year: 'numeric' } : {}), timeZone: 'Asia/Colombo' })
    .format(new Date(`${date}T12:00:00Z`))
}

function refreshTime(time: number): string {
  return new Intl.DateTimeFormat('en-LK', { hour: 'numeric', minute: '2-digit', second: '2-digit', hour12: true, timeZone: 'Asia/Colombo' }).format(new Date(time))
}
