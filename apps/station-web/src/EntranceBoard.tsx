import { useLayoutEffect, useMemo, useRef, useState } from 'react'
import { entranceRowLimit, fitEntranceRows, hasNotice, isNotice, type BoardRow } from './board.ts'
import { say, type Texts } from './messages.ts'
import { DisruptionNotice, Lines, PassiveState, StatusLine } from './parts.tsx'
import { describeRow, type Pill } from './rowText.ts'
import { formatClock } from './time.ts'

/** Gaps between the board's items, so a measured height includes the space that follows it. */
const noticeGap = 16
const rowGap = 12

type Props = {
  rows: BoardRow[]
  station: (id: string) => Texts
  /** A banner is showing above the board, so there is no room for the heading line. */
  banner: boolean
  /** When the saved timetable was downloaded; used by timetable-only rows. */
  savedAt: number
}

/**
 * Upcoming arrivals and departures for one station, in the order Scheduled, Destination, Platform,
 * Expected, Status. The scheduled time is never replaced by the expected one. Confirmed
 * cancellations and trains that no longer stop here come first as notices; the rest are rows that
 * keep their complete three-language text. Waiting areas use this same board.
 */
export function EntranceBoard({ rows, station, banner, savedAt }: Props) {
  const root = useRef<HTMLDivElement>(null)
  const items = useRef(new Map<string, HTMLElement>())
  const fixed = useRef<{ purpose: HTMLElement | null; labels: HTMLElement | null; more: HTMLElement | null }>({ purpose: null, labels: null, more: null })
  const [visible, setVisible] = useState<ReadonlySet<string> | null>(null)

  const notices = useMemo(() => rows.filter(hasNotice), [rows])
  const listed = useMemo(() => rows.filter((row) => !isNotice(row)), [rows])
  // Everything that takes room on the board, notices first: a notice and its row fit or go together
  // only by their own heights, so each has its own key.
  const entries = useMemo(() => [...notices.map((row) => ({ ...row, key: `${row.key}:notice` })), ...listed], [notices, listed])
  const timetableOnly = rows.some((row) => row.timetableOnly)
  const arrivals = listed.filter((row) => row.kind === 'arrival').length
  const purpose = arrivals === 0 ? 'departures' : arrivals === listed.length ? 'arrivals' : 'departuresAndArrivals'
  const showPurpose = !banner && notices.length === 0
  const latestObservation = rows.reduce<number | null>((latest, row) => (row.observedAt !== null && (latest === null || row.observedAt > latest) ? row.observedAt : latest), null)

  useLayoutEffect(() => {
    if (!root.current || entries.length === 0) return
    let active = true
    const measure = () => {
      const { purpose: heading, labels, more } = fixed.current
      if (!active || !root.current || !more) return
      // Measure every candidate before hiding any; language and font loading can change heights.
      const elements = entries.map((row) => items.current.get(row.key)!)
      elements.forEach((element) => { element.hidden = false })
      if (labels) labels.hidden = false
      more.hidden = false
      const heights = elements.map((element, i) => element.getBoundingClientRect().height + (entries[i].key.endsWith(':notice') ? noticeGap : rowGap))
      const fixedHeight = (heading ? heading.getBoundingClientRect().height + noticeGap : 0) + (labels ? labels.getBoundingClientRect().height + noticeGap : 0)
      const available = root.current.clientHeight - fixedHeight
      const allFit = entries.length <= entranceRowLimit && heights.reduce((a, b) => a + b, 0) <= available + rowGap
      const selected = allFit
        ? new Set(entries.map((row) => row.key))
        : fitEntranceRows(entries, heights, available - more.getBoundingClientRect().height - 8)
      elements.forEach((element, i) => { element.hidden = !selected.has(entries[i].key) })
      if (labels) labels.hidden = !listed.some((row) => selected.has(row.key))
      more.hidden = selected.size === entries.length
      setVisible((before) => (before && before.size === selected.size && [...selected].every((key) => before.has(key)) ? before : selected))
    }
    const observer = new ResizeObserver(measure)
    observer.observe(root.current)
    void document.fonts.ready.then(measure)
    measure()
    return () => { active = false; observer.disconnect() }
  }, [entries, station, listed])

  if (rows.length === 0) {
    return <PassiveState stateKey="no-services" title={say('noTrains')} detail={say('noTrainsDetail')} />
  }
  const hidden = (key: string) => visible !== null && !visible.has(key)
  const register = (key: string) => (element: HTMLElement | null) => {
    if (element) items.current.set(key, element)
    else items.current.delete(key)
  }
  const mixed = purpose === 'departuresAndArrivals'

  return (
    <div className="entrance-board" ref={root}>
      {showPurpose && (
        <div className="board-purpose" ref={(element) => { fixed.current.purpose = element }}>
          <Lines texts={say(purpose)} type="label" layout="inline" />
          {latestObservation !== null && <span className="observations">Observations {formatClock(latestObservation)}</span>}
        </div>
      )}
      {notices.map((row) => (
        <NoticeRow key={row.key} row={row} station={station} hidden={hidden(`${row.key}:notice`)} register={register(`${row.key}:notice`)} />
      ))}
      <div className="entrance" role="table">
        <div className="labels" role="row" ref={(element) => { fixed.current.labels = element }} hidden={listed.length === 0}>
          <Lines texts={say('scheduled')} type="small" />
          <Lines texts={say(purpose === 'arrivals' ? 'fromHeader' : 'destination')} type="small" />
          <Lines texts={say(timetableOnly ? 'plannedHeader' : 'platform')} type="small" />
          <Lines texts={say('expected')} type="small" />
          <Lines texts={say('status')} type="small" />
        </div>
        <div className="service-rows" role="rowgroup">
          {listed.map((row) => (
            <EntranceRow key={row.key} row={row} station={station} mixed={mixed} savedAt={savedAt} hidden={hidden(row.key)} register={register(row.key)} />
          ))}
        </div>
      </div>
      <p className="more-services" ref={(element) => { fixed.current.more = element }} hidden={visible === null || visible.size === entries.length}>
        <Lines texts={say('moreServices', { n: rows.filter((row) => !(visible?.has(row.key) || visible?.has(`${row.key}:notice`))).length })} type="meta" layout="inline" />
      </p>
    </div>
  )
}

function NoticeRow({ row, station, hidden, register }: { row: BoardRow; station: (id: string) => Texts; hidden: boolean; register: (element: HTMLElement | null) => void }) {
  return (
    <div className="entrance-item" hidden={hidden} ref={register}>
      <DisruptionNotice row={row} station={station} />
    </div>
  )
}

function EntranceRow({ row, station, mixed, savedAt, hidden, register }: { row: BoardRow; station: (id: string) => Texts; mixed: boolean; savedAt: number; hidden: boolean; register: (element: HTMLElement | null) => void }) {
  const described = describeRow(row, station, true)
  // A train whose read failed says so in one line, as in the frames; what is known about its time
  // stays in the Expected column ("Unknown · Read unavailable") and an ageing pill stays.
  const pills: Pill[] = row.refreshFailed
    ? [...described.pills.filter((pill) => pill.id === 'AGEING'), { id: 'update-unavailable', tone: 'neutral', icon: 'question', glyph: 'info', texts: say('updateUnavailable') }]
    : described.pills
  const notes = row.refreshFailed ? described.notes.filter((note) => !note.en.startsWith('Could not update')) : described.notes
  const other = station(row.otherStationId)
  const unknown = row.expectedAt === null && !row.timetableOnly
  // The warm tint marks a train that is late, ageing, changed platform or ends early. (The arrivals frame
  // leaves its late train plain; the other four frames with a late train tint it.)
  const tone = row.timetableOnly ? 'saved' : row.delayed || row.moved || row.endsEarly || row.evidence === 'AGEING' ? 'changed' : 'normal'
  return (
    <div className={`row row-${tone}`} role="row" hidden={hidden} ref={register}>
      <div className="cell cell-time" role="cell">
        <span className="time">
          <span className="visually-hidden">{say('scheduled').en} </span>
          {formatClock(row.scheduledAt)}
        </span>
        <span className="train-number">{row.trainNumber}</span>
      </div>
      <div className="cell cell-destination" role="cell">
        <Lines texts={mixed && row.kind === 'arrival' ? say('from', { station: other }) : other} type="row" />
      </div>
      <div className="cell cell-platform" role="cell">
        <span className="platform-number">{row.platform ?? '–'}</span>
        {row.timetableOnly ? (
          <span className="sub">planned</span>
        ) : row.moved ? (
          <span className="sub">was {row.moved.from}</span>
        ) : row.evidence === 'UNKNOWN' && row.platformConfirmedAt !== null ? (
          <span className="sub">conf. {formatClock(row.platformConfirmedAt)}</span>
        ) : (
          <span className="sub" />
        )}
      </div>
      <div className="cell cell-expected" role="cell">
        {row.timetableOnly ? (
          <>
            <span className="expected-word">—</span>
            <span className="sub">Saved {formatClock(savedAt)}</span>
          </>
        ) : unknown ? (
          <>
            <span className="expected-word">Unknown</span>
            <span className="sub">{row.observedAt !== null ? `Last ${formatClock(row.observedAt)}` : row.refreshFailed ? 'Read unavailable' : 'No observation'}</span>
          </>
        ) : (
          <>
            <span className={row.delayed ? 'expected-time late' : 'expected-time'}>
              <span className="visually-hidden">{say('expected').en} </span>
              {formatClock(row.expectedAt!)}
            </span>
            <span className="sub">{row.observedAt !== null && `Obs. ${formatClock(row.observedAt)}`}</span>
          </>
        )}
      </div>
      <div className="cell cell-status" role="cell">
        {row.timetableOnly && <StatusLine pill={{ id: 'timetable-only', tone: 'neutral', icon: 'question', glyph: 'info', texts: say('timetableStatus') }} />}
        {pills.map((pill) => (
          <StatusLine key={pill.id} pill={pill} />
        ))}
        {notes.map((note, i) => (
          <Lines key={i} texts={note} type="small" />
        ))}
      </div>
    </div>
  )
}
