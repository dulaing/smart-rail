import type { ReactNode } from 'react'
import { FreshnessLine } from '../design/FreshnessLine.tsx'
import { StatusBadge } from '../design/StatusBadge.tsx'
import type { Me } from '../auth'
import { ChangePanel } from './ChangePanel.tsx'
import { confirmedFacts, delayOf, evidenceOf, failureText, stationName } from './facts.ts'
import { usePolled, useNow } from './hooks.ts'
import { isJourneyId, loadJourney, type JourneySnapshot } from './journeyLoader.ts'
import { correctedNow } from './corridorLoader.ts'
import { publicReader } from './live.ts'
import { AppLink, ConfirmedCell, DelayCell } from './parts.tsx'
import { canChange, changeRights } from './rights.ts'
import { clockText, stopTimeText } from './time.ts'
import type { RunningResponse, Station } from './types.ts'
import './workspace.css'

/** One dated journey: its stops with schedule and expected times, the changes in force, and the change panel. */
export function JourneyWorkspace({ me, journeyId, onRefreshMe }: { me: Me; journeyId: string; onRefreshMe: () => void }) {
  const valid = isJourneyId(journeyId)
  const { data, refresh } = usePolled<JourneySnapshot>(`journey:${valid ? journeyId : 'invalid'}`, (signal, previous) =>
    valid
      ? loadJourney({
          reader: publicReader,
          journeyId,
          now: correctedNow(previous?.clockOffsetMs ?? null, Date.now()),
          previous,
          signal,
        })
      : Promise.resolve(emptySnapshot),
  )
  const now = useNow(data?.clockOffsetMs ?? null)

  const back = (
    <p>
      <AppLink to="/corridor">← Back to the corridor</AppLink>
    </p>
  )

  if (!valid) return <NotFound back={back} text="This address does not name a journey." />
  if (data === null) {
    return (
      <p role="status" className="muted">
        Loading the journey…
      </p>
    )
  }
  if (data.running === null) {
    if (data.runningFailure?.type === 'http' && data.runningFailure.status === 404) {
      return <NotFound back={back} text="No journey has this ID." />
    }
    return (
      <section className="stack">
        {back}
        <div className="notice">
          <p role="alert">
            The journey could not be read. {failureText(data.runningFailure)} This is not a permission problem.
          </p>
          <div className="actions">
            <button type="button" onClick={refresh}>
              Try again
            </button>
          </div>
        </div>
      </section>
    )
  }

  const running = data.running
  const evidence = evidenceOf(running, now)
  const lost = data.runningFailure?.type === 'network' && data.changesFailure?.type === 'network'
  const someFailed = data.runningFailure !== null || data.changesFailure !== null
  const rights = changeRights(me)

  return (
    <section className="stack" aria-label={`Train ${running.trainNumber}`}>
      {back}
      <div>
        <h2>
          Train {running.trainNumber} · {running.serviceDate}
        </h2>
        <p className="muted">
          Timetable: {running.timetable.label}. Running information: {running.runningLabel}. Times are Sri Lanka time.
        </p>
      </div>

      {lost ? (
        <div className="notice" role="alert">
          <p>
            <strong>Connection lost.</strong> This page shows what was read
            {data.readAt !== null ? ` at ${clockText(data.readAt)}` : ' earlier'}. It is not current and keeps trying.
          </p>
        </div>
      ) : (
        someFailed && (
          <p className="notice warning" role="status">
            {data.runningFailure ? 'The running information' : 'The confirmed changes'} could not be refreshed.{' '}
            {failureText(data.runningFailure ?? data.changesFailure)} What is shown may be older than it looks.
          </p>
        )
      )}

      <dl className="facts">
        <div>
          <dt>Observation</dt>
          <dd>
            <FreshnessLine
              observedAt={running.lastObservation?.observedAt ?? null}
              now={now}
              locale="en"
              thresholds={running.thresholds}
              offline={lost}
              formatTime={clockText}
            />
          </dd>
        </div>
        <div>
          <dt>Observed delay</dt>
          <dd>
            <DelayCell delay={delayOf(running, evidence)} evidence={evidence} />
          </dd>
        </div>
        <div>
          <dt>Confirmed by staff</dt>
          <dd>
            <ConfirmedCell facts={confirmedFacts(running, data.stations)} />
            {data.changes && <span className="sub">Journey version {data.changes.journeyVersion}</span>}
          </dd>
        </div>
      </dl>

      <StopsTable running={running} stations={data.stations} evidenceUnknown={evidence === 'UNKNOWN'} />

      {canChange(rights) ? (
        data.changes ? (
          <ChangePanel
            // A new journey starts a new, empty change.
            key={running.journeyId}
            journeyId={running.journeyId}
            running={running}
            changes={data.changes}
            stations={data.stations}
            rights={rights}
            onChanged={refresh}
            onRefreshMe={onRefreshMe}
          />
        ) : (
          <p className="notice warning" role="status">
            The confirmed changes could not be read, so changes cannot be made from this page right now.{' '}
            {failureText(data.changesFailure)}
          </p>
        )
      ) : (
        <p className="notice neutral">
          You can view this journey but your account cannot change it, so there are no change controls here.
        </p>
      )}
    </section>
  )
}

const emptySnapshot: JourneySnapshot = {
  stations: [],
  running: null,
  changes: null,
  runningFailure: { type: 'http', status: 404, detail: null },
  changesFailure: null,
  clockOffsetMs: null,
  readAt: null,
}

function NotFound({ back, text }: { back: ReactNode; text: string }) {
  return (
    <section className="stack">
      {back}
      <p className="notice" role="alert">
        {text}
      </p>
    </section>
  )
}

function StopsTable({ running, stations, evidenceUnknown }: { running: RunningResponse; stations: readonly Station[]; evidenceUnknown: boolean }) {
  const date = running.serviceDate
  return (
    <div className="table-wrap" role="region" aria-label="Stops" tabIndex={0}>
      <table>
        <caption className="sr-only">Stops in running order</caption>
        <thead>
          <tr>
            <th scope="col">Stop</th>
            <th scope="col">Scheduled arrive / depart</th>
            <th scope="col">Expected arrive / depart</th>
            <th scope="col">Platform</th>
            <th scope="col">Service</th>
          </tr>
        </thead>
        <tbody>
          {running.stops.map((stop) => (
            <tr key={stop.stopId}>
              <th scope="row">
                {stop.sequence}. {stationName(stations, stop.stationId)}
              </th>
              <td>
                {stopTimeText(stop.scheduledArrival, date)} / {stopTimeText(stop.scheduledDeparture, date)}
              </td>
              <td>
                {!stop.served ? (
                  <span className="muted">No expected time</span>
                ) : evidenceUnknown ? (
                  <span className="muted">Not known</span>
                ) : (
                  `${stopTimeText(stop.estimatedArrival, date)} / ${stopTimeText(stop.estimatedDeparture, date)}`
                )}
              </td>
              <td>
                {stop.confirmedPlatform ? (
                  <span className="badges">
                    <span>{stop.confirmedPlatform}</span>
                    <StatusBadge status="platform-change" locale="en" />
                    <span className="sub">
                      planned {stop.plannedPlatform ?? 'none'}
                      {stop.confirmedPlatformAt ? `, confirmed ${clockText(stop.confirmedPlatformAt)}` : ''}
                    </span>
                  </span>
                ) : (
                  (stop.plannedPlatform ?? <span className="muted">None</span>)
                )}
              </td>
              <td>{stop.served ? 'Served' : 'Not served'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
