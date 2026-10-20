// The board shows Sri Lanka time whatever the screen's own time zone is set to, and a service
// date is a Colombo calendar date. Plain functions with no React so the tests can run them.

const zone = 'Asia/Colombo'

const clockFormat = new Intl.DateTimeFormat('en-GB', { timeZone: zone, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })
const dateFormat = new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit' })

const dateParts = new Intl.DateTimeFormat('en-GB', { timeZone: zone, weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' })

/** "Mon 12 Oct 2026" in Colombo time, as the board header shows it. */
export const formatDate = (instant: number): string => {
  const part = Object.fromEntries(dateParts.formatToParts(new Date(instant)).map((p) => [p.type, p.value]))
  return `${part.weekday} ${part.day} ${part.month} ${part.year}`
}

/** HH:mm in Colombo time. */
export const formatClock = (instant: number | Date | string): string => clockFormat.format(new Date(instant))

/** The Colombo calendar date of an instant, as yyyy-MM-dd. */
export const colomboDate = (instant: number): string => dateFormat.format(new Date(instant))

/** Moves a yyyy-MM-dd date by whole days. */
export function addDays(date: string, days: number): string {
  const moved = new Date(`${date}T00:00:00Z`)
  moved.setUTCDate(moved.getUTCDate() + days)
  return moved.toISOString().slice(0, 10)
}

/**
 * The service dates a board needs: yesterday for trains that left before midnight and are still
 * running, today, and tomorrow so the next day is already saved when midnight passes.
 */
export const serviceDatesAround = (instant: number): string[] => {
  const today = colomboDate(instant)
  return [addDays(today, -1), today, addDays(today, 1)]
}
