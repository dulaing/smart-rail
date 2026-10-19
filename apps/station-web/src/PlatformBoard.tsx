import type { BoardRow, CallingStop, PlatformView } from './board.ts'
import { say, type Texts } from './messages.ts'
import { ArrowIcon, Lines, StatusPill } from './parts.tsx'
import { describeRow } from './rowText.ts'
import { formatClock } from './time.ts'

type Props = {
  platform: string
  view: PlatformView
  /** The stops after this one for the next train, in order. */
  calls: CallingStop[]
  station: (id: string) => Texts
}

/**
 * One platform: its next train large, with where it calls and what has changed, then the notices
 * that belong to this platform (a train moved away from it or onto it, a cancellation, a train
 * that no longer comes here). A train moved to another platform stays a notice here until the
 * evidence shows it gone, so a waiting passenger can understand where it went.
 */
export function PlatformBoard({ platform, view, calls, station }: Props) {
  const { next, notices } = view
  return (
    <div className="platform-board">
      <section className="hero" aria-labelledby="hero-title">
        <div className="platform-id">
          <Lines texts={say('platform')} />
          <span className="platform-number huge">{platform}</span>
        </div>
        <div className="hero-body">
          <h2 id="hero-title" className="hero-title">
            <Lines texts={say('nextTrain')} />
          </h2>
          {next ? <NextTrain row={next} calls={calls} station={station} /> : <p className="empty"><Lines texts={say('noNextTrain')} /></p>}
        </div>
      </section>

      {notices.length > 0 && (
        <section className="notices" aria-label="Notices">
          <ul>
            {notices.map((row) => (
              <NoticeRow key={row.key} row={row} station={station} />
            ))}
          </ul>
        </section>
      )}
    </div>
  )
}

function NextTrain({ row, calls, station }: { row: BoardRow; calls: CallingStop[]; station: (id: string) => Texts }) {
  const { pills, notes } = describeRow(row, station)
  const other = station(row.otherStationId)
  return (
    <div className="next-train">
      <div className="next-main">
        <ArrowIcon kind={row.kind} />
        <span className="time big">
          <span className="visually-hidden">{say('scheduled').en} </span>
          {formatClock(row.scheduledAt)}
        </span>
        {row.expectedAt !== null && (
          <span className={row.delayed ? 'time big expected late' : 'time big expected'}>
            <span className="visually-hidden">{say('expected').en} </span>
            {formatClock(row.expectedAt)}
          </span>
        )}
        <span className="train-number">{row.trainNumber}</span>
      </div>
      <div className="next-where">
        <Lines texts={row.kind === 'arrival' ? say('from', { station: other }) : other} />
        {(row.kind === 'arrival' || row.startsHere) && (
          <span className="sub">
            <Lines texts={say(row.kind === 'arrival' ? 'endsHere' : 'startsHere')} inline />
          </span>
        )}
      </div>
      <div className="next-status">
        {pills.map((pill) => (
          <StatusPill key={pill.id} pill={pill} />
        ))}
        {notes.map((note, i) => (
          <span key={i} className="note-line">
            <Lines texts={note} />
          </span>
        ))}
      </div>
      {calls.length > 0 && (
        <div className="calls">
          <h3>
            <Lines texts={say('callsAt')} inline />
          </h3>
          <ol>
            {calls.map((stop) => (
              <li key={stop.stopId} className={stop.served ? undefined : 'stop-off'}>
                {stop.served && stop.scheduledAt !== null ? (
                  <span className="time small">{formatClock(stop.scheduledAt)}</span>
                ) : (
                  <span className="sub">
                    <Lines texts={say('notServed')} inline />
                  </span>
                )}
                <Lines texts={station(stop.stationId)} />
              </li>
            ))}
          </ol>
        </div>
      )}
    </div>
  )
}

function NoticeRow({ row, station }: { row: BoardRow; station: (id: string) => Texts }) {
  const { pills, notes } = describeRow(row, station)
  return (
    <li className="notice">
      <span className="time">{formatClock(row.scheduledAt)}</span>
      <span className="train-number">{row.trainNumber}</span>
      <Lines texts={station(row.otherStationId)} />
      <span className="notice-status">
        {pills.map((pill) => (
          <StatusPill key={pill.id} pill={pill} />
        ))}
        {notes.map((note, i) => (
          <span key={i} className="note-line">
            <Lines texts={note} />
          </span>
        ))}
      </span>
    </li>
  )
}
