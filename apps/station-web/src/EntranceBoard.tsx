import { useLayoutEffect, useRef, useState } from 'react'
import { entranceRowLimit, fitEntranceRows, type BoardRow } from './board.ts'
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
  const root = useRef<HTMLDivElement>(null)
  const table = useRef<HTMLTableElement>(null)
  const more = useRef<HTMLParagraphElement>(null)
  const [visible, setVisible] = useState<ReadonlySet<string> | null>(null)
  useLayoutEffect(() => {
    if (!root.current || !table.current || !more.current) return
    let active = true
    const measure = () => {
      if (!active || !root.current || !table.current || !more.current) return
      const list = [...table.current!.tBodies[0].rows]
      // Measure all candidate rows before hiding any; language/font loading can change heights.
      list.forEach(row => { row.hidden = false })
      more.current!.hidden = false
      const heights = list.map(row => row.getBoundingClientRect().height)
      const header = table.current!.tHead!.getBoundingClientRect().height
      const available = root.current!.clientHeight - header
      const allFit = rows.length <= entranceRowLimit && heights.reduce((a, b) => a + b, 0) <= available
      const selected = allFit ? new Set(rows.map(row => row.key))
        : fitEntranceRows(rows, heights, available - more.current!.getBoundingClientRect().height - 8)
      list.forEach((row, i) => { row.hidden = !selected.has(rows[i].key) })
      more.current!.hidden = selected.size === rows.length
      setVisible(before => before && before.size === selected.size && [...selected].every(key => before.has(key)) ? before : selected)
    }
    const observer = new ResizeObserver(measure)
    observer.observe(root.current)
    void document.fonts.ready.then(measure)
    measure()
    return () => { active = false; observer.disconnect() }
  }, [rows, station])
  if (rows.length === 0) {
    return (
      <p className="empty">
        <Lines texts={say('noTrains')} />
      </p>
    )
  }
  return (
    <div className="entrance-board" ref={root}>
    <table className="entrance" ref={table}>
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
        {rows.map((row) => (
          <EntranceRow key={row.key} row={row} station={station} hidden={visible !== null && !visible.has(row.key)} />
        ))}
      </tbody>
    </table>
    <p className="more-services" ref={more} hidden={visible === null || visible.size === rows.length}>
      <Lines texts={say('moreServices', { n: rows.length - (visible?.size ?? 0) })} inline />
    </p>
    </div>
  )
}

function EntranceRow({ row, station, hidden }: { row: BoardRow; station: (id: string) => Texts; hidden: boolean }) {
  const { pills, notes } = describeRow(row, station)
  const off = row.cancelled || row.unservedHere
  const other = station(row.otherStationId)
  return (
    <tr className={off ? 'row row-off' : 'row'} hidden={hidden}>
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
