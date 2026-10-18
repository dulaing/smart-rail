import { formatClock } from './time.ts'

/**
 * The board clock, to the minute, in Colombo time. `now` is already corrected with the server
 * clock; the text only changes when the minute does, so the screen never flickers, and it is plain
 * text rather than a chatty live region.
 */
export function Clock({ now }: { now: number }) {
  const text = formatClock(now)
  return (
    <time className="clock" dateTime={text}>
      {text}
    </time>
  )
}
