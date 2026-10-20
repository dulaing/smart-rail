import { useLayoutEffect, useRef, type ReactNode } from 'react'
import { hasNotice, type BoardRow, type CallingStop, type PlatformView } from './board.ts'
import { say, sayShared, type Texts } from './messages.ts'
import { DisruptionNotice, Lines, NoticeCard } from './parts.tsx'
import { describeRow, type Pill } from './rowText.ts'
import { formatClock } from './time.ts'
import type { Tone } from '../../../shared/design/generated/status.ts'

type Props = {
  platform: string
  view: PlatformView
  /** The stops after this one for the next train, in order. */
  calls: CallingStop[]
  station: (id: string) => Texts
  /** When the saved timetable was downloaded; used when only the timetable is known. */
  savedAt: number
  /** When the live feed last updated, for the notice when there is no train; null when not live. */
  updatedAt?: number | null
  /** A banner (live updates lost, saved timetable out of date) takes the notice slot. */
  banner: ReactNode
  /** Replaces "no next train" when there is no timetable to read (offline without a saved copy). */
  emptyTitle?: Texts
}

const toneRank: Record<Tone, number> = { danger: 4, warning: 3, accent: 2, neutral: 1, success: 0 }
const lead = (pills: Pill[]): Pill | undefined => [...pills].sort((a, b) => toneRank[b.tone] - toneRank[a.tone])[0]

/** The small English line at the top right of the service card: what the time is based on. */
function basis(row: BoardRow, savedAt: number): string {
  if (row.timetableOnly) return `Saved ${formatClock(savedAt)}`
  if (row.cancelled) return 'No current ETA'
  if (row.unservedHere) return row.kind === 'arrival' ? 'No arrival here' : 'No departure here'
  if (row.evidence === 'UNKNOWN') return row.observedAt === null ? 'No observation' : `Last obs. ${formatClock(row.observedAt)}`
  return row.observedAt === null ? '' : `Obs. ${formatClock(row.observedAt)}`
}

function purposeOf(row: BoardRow): Texts {
  if (row.timetableOnly) return say('savedTimetable')
  if (row.cancelled) return say('cancelledService')
  if (row.unservedHere) return say('affectedService')
  if (row.endsHere) return say('finalArrival')
  return say('nextTrain')
}

/**
 * One platform: its number, its next train large with scheduled and expected times, then one
 * notice for what has changed or what is not known. A train moved to another platform stays a
 * notice here until the evidence shows it gone, so a waiting passenger can understand where it
 * went; a cancelled train or one that no longer stops here is shown the same way.
 */
export function PlatformBoard({ platform, view, calls, station, savedAt, updatedAt: boardUpdatedAt = null, banner, emptyTitle }: Props) {
  const { next, notices } = view
  const root = useRef<HTMLDivElement>(null)
  // Extra notices are whole cards: list as many as fit below the first one, never a cut-off card.
  useLayoutEffect(() => {
    const element = root.current
    if (!element) return
    const fit = () => {
      const extras = [...element.querySelectorAll<HTMLElement>('[data-extra]')]
      extras.forEach((extra) => { extra.hidden = false })
      extras.forEach((extra) => { extra.hidden = extra.offsetTop + extra.offsetHeight > element.clientHeight })
    }
    fit()
    const observer = new ResizeObserver(fit)
    observer.observe(element)
    void document.fonts.ready.then(fit)
    return () => observer.disconnect()
  })
  // A train with a notice but no next train (cancelled, no longer served) is shown in the hero.
  const subject = next ?? notices[0] ?? null
  const described = subject ? describeRow(subject, station) : null
  const status = described && lead(described.pills)
  // A moved train gets the sentence the frames use: where to go now, or where it went.
  const move = subject && !hasNotice(subject) ? subject.moved : null
  const moveTitle = move ? say(move.to === platform ? 'movedHere' : 'movedAway', { to: move.to, from: move.from }) : null
  const moveConfirmed = move && move.confirmedAt !== null ? [sayShared('change.confirmed', { time: formatClock(move.confirmedAt) })] : []
  const confirmed = subject && subject.confirmedChangeAt !== null ? [sayShared('change.confirmed', { time: formatClock(subject.confirmedChangeAt) })] : []
  const updatedAt = subject ? (subject.observedAt ?? subject.checkedAt) : null
  const extraNotices = subject ? notices.filter((row) => row !== subject) : notices

  return (
    <div className="platform-board" ref={root}>
      <section className="hero" aria-labelledby="hero-title">
        <div className="platform-id">
          <Lines texts={say("platform")} type="notice" />
          <span className="platform-number huge">{platform}</span>
        </div>
        <div className="hero-body">
          {subject ? (
            <ServiceCard row={subject} calls={next ? calls : []} station={station} savedAt={savedAt} />
          ) : (
            <div className="service-empty">
              <h2 id="hero-title">
                <Lines texts={emptyTitle ?? say('noNextTrain')} type="hero" />
              </h2>
            </div>
          )}
        </div>
      </section>

      {banner ?? null}
      {!banner && subject && hasNotice(subject) && <DisruptionNotice row={subject} station={station} />}
      {!banner && moveTitle && <NoticeCard tone="accent" glyph="info" title={moveTitle} attribution={moveConfirmed} />}
      {!banner && !moveTitle && status && described && !hasNotice(subject!) && (
        <NoticeCard
          tone={status.id === 'on-time' ? 'neutral' : status.tone}
          glyph={status.glyph}
          title={status.texts}
          lines={described.notes.filter((note) => !confirmed.some((c) => c.en === note.en))}
          attribution={confirmed.length > 0 ? confirmed : updatedAt !== null ? [sayShared('freshness.current', { time: formatClock(updatedAt) })] : []}
        />
      )}
      {!banner && !status && !subject && (
        <NoticeCard tone="neutral" glyph="info" title={say('noTrains')} attribution={boardUpdatedAt === null ? [] : [sayShared('freshness.current', { time: formatClock(boardUpdatedAt) })]} reserveAttribution />
      )}
      {!banner && extraNotices.map((row) => <div key={row.key} data-extra><ExtraNotice row={row} station={station} /></div>)}
    </div>
  )
}

function ServiceCard({ row, calls, station, savedAt }: { row: BoardRow; calls: CallingStop[]; station: (id: string) => Texts; savedAt: number }) {
  const other = station(row.otherStationId)
  const text = basis(row, savedAt)
  const expected = row.expectedAt !== null ? formatClock(row.expectedAt) : row.timetableOnly || row.cancelled || row.unservedHere ? '—' : 'Unknown'
  return (
    <div className="service-card">
      <div className="service-head">
        <span className="train-number">{row.trainNumber}</span>
        <h2 id="hero-title" className="purpose">
          <Lines texts={purposeOf(row)} type="meta" layout="inline" />
        </h2>
        {text && <span className="basis">{text}</span>}
      </div>
      <div className="service-where">
        <Lines texts={row.kind === 'arrival' && !row.endsHere ? say('from', { station: other }) : other} type="hero" />
        <Calls calls={calls.filter((stop) => stop.stationId !== row.otherStationId)} station={station} />
      </div>
      <div className="service-times">
        <div className="time-block">
          <Lines texts={say('scheduled')} type="meta" layout="inline" />
          <span className="time">
            <span className="visually-hidden">{say('scheduled').en} </span>
            {formatClock(row.scheduledAt)}
          </span>
        </div>
        <div className="time-block">
          <Lines texts={say('expected')} type="meta" layout="inline" />
          <span className={row.expectedAt === null ? 'time expected word' : row.delayed ? 'time expected late' : 'time expected'}>
            <span className="visually-hidden">{say('expected').en} </span>
            {expected}
          </span>
        </div>
      </div>
    </div>
  )
}

/**
 * Where the train calls next. The approved frame leaves the right of the destination free, so as
 * many whole stops as fit are listed there; a stop is never cut in half.
 */
function Calls({ calls, station }: { calls: CallingStop[]; station: (id: string) => Texts }) {
  const root = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => {
    const element = root.current
    if (!element) return
    const fit = () => {
      const items = [...element.querySelectorAll<HTMLElement>('li')]
      items.forEach((item) => { item.hidden = false })
      items.forEach((item) => { item.hidden = item.offsetTop + item.offsetHeight > element.clientHeight })
      element.style.visibility = items.some((item) => !item.hidden) ? 'visible' : 'hidden'
    }
    fit()
    void document.fonts.ready.then(fit)
  }, [calls])
  if (calls.length === 0) return null
  return (
    <div className="calls" ref={root}>
      <h3>
        <Lines texts={say('callsAt')} type="meta" layout="inline" />
      </h3>
      <ol>
        {calls.map((stop) => (
          <li key={stop.stopId} className={stop.served ? undefined : 'stop-off'}>
            {stop.served && stop.scheduledAt !== null ? (
              <span className="time small">{formatClock(stop.scheduledAt)}</span>
            ) : (
              <span className="sub">
                <Lines texts={say('notServed')} type="meta" layout="inline" />
              </span>
            )}
            <Lines texts={station(stop.stationId)} type="meta" />
          </li>
        ))}
      </ol>
    </div>
  )
}

function ExtraNotice({ row, station }: { row: BoardRow; station: (id: string) => Texts }) {
  const { pills, notes } = describeRow(row, station)
  const top = lead(pills)
  if (!top) return null
  return <NoticeCard tone={top.tone} glyph={top.glyph} title={top.texts} lines={notes} />
}

