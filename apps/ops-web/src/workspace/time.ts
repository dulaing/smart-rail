// Service dates and clock times. Sri Lanka has one offset (UTC+05:30, no daylight saving), the
// same fixed zone the API uses to turn a timetable's local times into instants.

const colomboOffsetMs = 5.5 * 60 * 60 * 1000
const dayMs = 24 * 60 * 60 * 1000

/** The service date (yyyy-MM-dd) that contains this instant in Sri Lanka. */
export function colomboDate(ms: number): string {
  return new Date(ms + colomboOffsetMs).toISOString().slice(0, 10)
}

export function addDays(date: string, days: number): string {
  return new Date(Date.parse(`${date}T00:00:00Z`) + days * dayMs).toISOString().slice(0, 10)
}

/** The instant that service date starts in Sri Lanka, as epoch milliseconds. */
export function startOfColomboDay(date: string): number {
  return Date.parse(`${date}T00:00:00Z`) - colomboOffsetMs
}

const clockFormat = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Asia/Colombo',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
})

/** HH:mm in Sri Lanka time. */
export function clockText(time: Date | string | number): string {
  return clockFormat.format(new Date(time))
}

/** HH:mm, with the date added when the instant falls on another day than the service date. */
export function stopTimeText(instant: string | null, serviceDate: string): string {
  if (!instant) return '—'
  const time = Date.parse(instant)
  const date = colomboDate(time)
  return date === serviceDate ? clockText(time) : `${clockText(time)} (${date})`
}

/** HH:mm for a timetable instant that may be absent. */
export function timeOrDash(instant: string | null): string {
  return instant ? clockText(instant) : '—'
}

/** Whole minutes from seconds, rounded to the nearest minute. */
export function minutesOf(seconds: number): number {
  return Math.round(seconds / 60)
}
