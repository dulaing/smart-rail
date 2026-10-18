import { useEffect, useState } from 'react'

const format = (time: Date) =>
  `${String(time.getHours()).padStart(2, '0')}:${String(time.getMinutes()).padStart(2, '0')}`

/**
 * The board clock, to the minute. It checks every few seconds but only redraws when the minute
 * changes, so the screen never flickers, and it is plain text rather than a chatty live region.
 */
export function Clock() {
  const [text, setText] = useState(() => format(new Date()))
  useEffect(() => {
    const timer = window.setInterval(() => setText(format(new Date())), 5000)
    return () => window.clearInterval(timer)
  }, [])
  return (
    <time className="clock" dateTime={text}>
      {text}
    </time>
  )
}
