import { useMemo } from 'react'
import { FreshnessLine } from '../design/FreshnessLine.tsx'
import type { Me } from '../auth'
import { colomboDate, clockText, timeOrDash } from './time.ts'
import { correctedNow, loadCorridor, type CorridorRow, type CorridorSnapshot } from './corridorLoader.ts'
import { confirmedFacts, delayOf, evidenceOf, failureText, hasConfirmedChange, lastKnownStation, stationName } from './facts.ts'
import { usePolled, useNow } from './hooks.ts'
import { publicReader } from './live.ts'
import { corridorStations } from './rights.ts'
import { AppLink, ConfirmedCell, DelayCell } from './parts.tsx'
import './workspace.css'

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

  const rows =
    mode === 'all'
      ? data.rows
      : data.rows.filter((row) => row.running !== null && hasConfirmedChange(confirmedFacts(row.running, data.stations)))
  const lost = data.status === 'lost'

  return (
    <section className="stack" aria-label={mode === 'all' ? 'Corridor trains' : 'Trains with confirmed changes'}>
      <p className="muted">
        Service date {data.today}, plus any train from the day before that is still running after midnight. Times are
        Sri Lanka time. Running information is SmartRail simulation, and refreshes every 15 seconds while this page is
        open.
      </p>

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

      {rows.length === 0 ? (
        <p className="notice neutral" role="status">
          {mode === 'all'
            ? 'No trains are scheduled on these service dates for your access. This is a successful, empty answer.'
            : 'No confirmed changes are in force on the trains listed here.'}
        </p>
      ) : (
        <div className="table-wrap" role="region" aria-label="Train list" tabIndex={0}>
          <table>
            <thead>
              <tr>
                <th scope="col">Train</th>
                <th scope="col">Route</th>
                <th scope="col">Last known station</th>
                <th scope="col">Observed delay</th>
                <th scope="col">Confirmed by staff</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <TrainRow key={row.journey.journeyId} row={row} snapshot={data} now={now} offline={lost} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}

function TrainRow({ row, snapshot, now, offline }: { row: CorridorRow; snapshot: CorridorSnapshot; now: number; offline: boolean }) {
  const { journey, running } = row
  const first = journey.stops[0]
  const last = journey.stops[journey.stops.length - 1]
  const evidence = evidenceOf(running, now)

  return (
    <tr>
      <th scope="row">
        <AppLink to={`/corridor/journeys/${journey.journeyId}`}>{journey.trainNumber}</AppLink>
        <span className="sub">
          {journey.serviceDate}
          {journey.serviceDate !== snapshot.today ? ' (left yesterday)' : ''}
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
        <td colSpan={3}>
          <span className="problem" role="status">
            {row.readFailed ? 'Could not read running information for this train.' : 'No running information yet.'}
          </span>
        </td>
      ) : (
        <>
          <td>
            {lastKnownStation(running, snapshot.stations) ?? <span className="muted">No observation yet</span>}
            <span className="sub">
              <FreshnessLine
                observedAt={running.lastObservation?.observedAt ?? null}
                now={now}
                locale="en"
                thresholds={running.thresholds}
                offline={offline}
                formatTime={clockText}
              />
            </span>
            {row.readFailed && (
              <span className="sub problem" role="status">
                Could not refresh: showing the earlier reading.
              </span>
            )}
          </td>
          <td>
            <DelayCell delay={delayOf(running, evidence)} evidence={evidence} />
          </td>
          <td>
            <ConfirmedCell facts={confirmedFacts(running, snapshot.stations)} />
          </td>
        </>
      )}
    </tr>
  )
}
