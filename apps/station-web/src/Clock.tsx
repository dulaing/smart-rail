import { formatClock, formatDate } from './time.ts'

/**
 * The board clock, to the minute, in Colombo time, with the date beneath. `now` is already
 * corrected with the server clock; the text only changes when the minute does, so the screen never
 * flickers, and it is plain text rather than a chatty live region.
 */
export function Clock({ now }: { now: number }) {
  const text = formatClock(now)
  return (
    <div className="board-clock">
      <time className="clock" dateTime={text}>
        {text}
      </time>
      <span className="board-date">{formatDate(now)}</span>
    </div>
  )
}
