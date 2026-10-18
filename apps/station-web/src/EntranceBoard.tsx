import { entranceRowLimit, type BoardRow } from './board.ts'
import { say, type Texts } from './messages.ts'
import { Lines, StatusPill, ArrowIcon } from './parts.tsx'
import { describeRow } from './rowText.ts'
import { formatClock } from './time.ts'

/**
 * Upcoming arrivals and departures for one station, in the order Scheduled, Platform,
 * Destination, Status. Time, platform and the arrow carry the first meaning; the scheduled time is
 * never replaced by the expected one. Waiting areas use this same board.
 */
export function EntranceBoard({ rows, station }: { rows: BoardRow[]; station: (id: string) => Texts }) {
  const shown = rows.slice(0, entranceRowLimit)
  if (shown.length === 0) {
    return (
      <p className="empty">
        <Lines texts={say('noTrains')} />
      </p>
    )
  }
  return (
    <table className="entrance">
      <thead>
        <tr>
          <th scope="col">
            <Lines texts={say('scheduled')} />
          </th>
          <th scope="col">
            <Lines texts={say('platform')} />
          </th>
          <th scope="col" className="wide">
            <Lines texts={say('train')} />
          </th>
          <th scope="col" className="wide">
            <Lines texts={say('status')} />
          </th>
        </tr>
      </thead>
      <tbody>
        {shown.map((row) => (
          <EntranceRow key={row.key} row={row} station={station} />
        ))}
      </tbody>
    </table>
  )
}

function EntranceRow({ row, station }: { row: BoardRow; station: (id: string) => Texts }) {
  const { pills, notes } = describeRow(row, station)
  const off = row.cancelled || row.unservedHere
  const other = station(row.otherStationId)
  return (
    <tr className={off ? 'row row-off' : 'row'}>
      <td className="cell-time">
        <ArrowIcon kind={row.kind} />
        <span className="time">
          <span className="visually-hidden">{say('scheduled').en} </span>
          {formatClock(row.scheduledAt)}
        </span>
        {row.expectedAt !== null && (
          <span className={row.delayed ? 'time expected late' : 'time expected'}>
            <span className="visually-hidden">{say('expected').en} </span>
            {formatClock(row.expectedAt)}
          </span>
        )}
      </td>
      <td className="cell-platform">
        <span className="platform-number">{row.platform ?? '–'}</span>
      </td>
      <td className="cell-train">
        <span className="train-number">{row.trainNumber}</span>
        <Lines texts={row.kind === 'arrival' ? say('from', { station: other }) : other} />
        {(row.kind === 'arrival' || row.startsHere) && (
          <span className="sub">
            <Lines texts={say(row.kind === 'arrival' ? 'endsHere' : 'startsHere')} inline />
          </span>
        )}
      </td>
      <td className="cell-status">
        {pills.map((pill) => (
          <StatusPill key={pill.id} pill={pill} />
        ))}
        {notes.map((note, i) => (
          <span key={i} className="note-line">
            <Lines texts={note} />
          </span>
        ))}
      </td>
    </tr>
  )
}
