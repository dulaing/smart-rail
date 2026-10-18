import { mapLimited, type ReadFailure, type Reader } from './api.ts'
import { addDays, colomboDate, startOfColomboDay } from './time.ts'
import type { JourneysResponse, RunningResponse, ScheduledJourney, Station } from './types.ts'

// One refresh of the corridor overview. There is no corridor endpoint: the overview is the public
// timetable reads (journeys of a service date) plus one running read per journey, composed here
// with a small limit on how many run at once.

/** How many running reads one refresh keeps in flight. */
export const maxParallelReads = 4

/**
 * A train that left on the previous service date can still be running after midnight. Earlier
 * dates are not needed: no pilot pattern runs for more than a night.
 */
export const serviceDatesBack = 1

export type CorridorRow = {
  journey: ScheduledJourney
  /** The last running answer for this journey, kept when a later read fails. */
  running: RunningResponse | null
  /** True when the latest attempt to read this journey's running information failed. */
  readFailed: boolean
}

export type CorridorSnapshot = {
  /** The Sri Lanka service date this refresh treated as today. */
  today: string
  stations: Station[]
  rows: CorridorRow[]
  /** False until at least one timetable read has worked; the screen then shows an error, not "no trains". */
  hasTimetable: boolean
  /**
   * ok: every read in this refresh worked. partial: some failed, so some rows are older than the
   * rest. lost: every read failed on the network, so nothing here is current.
   */
  status: 'ok' | 'partial' | 'lost'
  failedReads: number
  /** Milliseconds to add to the device clock to match the server; null until a response said. */
  clockOffsetMs: number | null
  /** The timetable that each date's journeys came from, kept so one failed date keeps its last list. */
  journeysByDate: Record<string, ScheduledJourney[]>
  /** When something was last read successfully, on the corrected clock; null if nothing ever was. */
  readAt: number | null
  /** The failure behind a total outage or an HTTP error, for the screen to describe. */
  lastFailure: ReadFailure | null
}

type Inputs = {
  reader: Reader
  /** The corrected current time in epoch milliseconds. */
  now: number
  /** The previous refresh, so one failed read keeps its last good answer. */
  previous: CorridorSnapshot | null
  /** Only journeys that stop at one of these stations; null lists the whole corridor. */
  stationIds: ReadonlySet<string> | null
  signal?: AbortSignal
}

/** A journey from an earlier date belongs on today's overview only while it is still running past midnight. */
export function runsIntoDay(journey: ScheduledJourney, date: string): boolean {
  const last = journey.stops[journey.stops.length - 1]
  const end = last?.scheduledArrival ?? last?.scheduledDeparture
  return end !== null && end !== undefined && Date.parse(end) >= startOfColomboDay(date)
}

export function stopsAt(journey: ScheduledJourney, stationIds: ReadonlySet<string> | null): boolean {
  return stationIds === null || journey.stops.some((s) => stationIds.has(s.stationId))
}

export async function loadCorridor({ reader, now, previous, stationIds, signal }: Inputs): Promise<CorridorSnapshot> {
  const today = colomboDate(now)
  const dates = Array.from({ length: serviceDatesBack + 1 }, (_, i) => addDays(today, i - serviceDatesBack))
  let failedReads = 0
  let attempted = 0
  let networkFailures = 0
  let lastFailure: ReadFailure | null = null
  let clockOffsetMs = previous?.clockOffsetMs ?? null

  const failed = (failure: ReadFailure) => {
    failedReads += 1
    if (failure.type === 'network') networkFailures += 1
    lastFailure = failure
  }

  attempted += 1
  const stationRead = await reader.read<Station[]>('/api/stations', { signal })
  if (stationRead.kind === 'failed') failed(stationRead.failure)
  const stations = stationRead.kind === 'ok' ? stationRead.body : (previous?.stations ?? [])

  const journeysByDate: Record<string, ScheduledJourney[]> = {}
  let hasTimetable = false
  const dateReads = await mapLimited(dates, maxParallelReads, (date) => {
    attempted += 1
    return reader.read<JourneysResponse>(`/api/journeys?serviceDate=${date}`, { signal })
  })
  dates.forEach((date, i) => {
    const read = dateReads[i]
    if (read.kind === 'ok') {
      journeysByDate[date] = read.body.journeys
      hasTimetable = true
    } else {
      failed(read.failure)
      // Keep the last list for a date that cannot be read now, but only while it is still wanted.
      const earlier = previous?.journeysByDate[date]
      if (earlier) {
        journeysByDate[date] = earlier
        hasTimetable = true
      }
    }
  })

  const wanted = dates.flatMap((date) =>
    (journeysByDate[date] ?? []).filter(
      (journey) => (date === today || runsIntoDay(journey, today)) && stopsAt(journey, stationIds),
    ),
  )

  const previousRows = new Map(previous?.rows.map((row) => [row.journey.journeyId, row]))
  const rows = await mapLimited(wanted, maxParallelReads, async (journey): Promise<CorridorRow> => {
    attempted += 1
    const read = await reader.read<RunningResponse>(`/api/journeys/${journey.journeyId}/running`, { signal })
    if (read.kind === 'ok') {
      if (read.serverTime) {
        const serverMs = Date.parse(read.serverTime)
        if (!Number.isNaN(serverMs)) clockOffsetMs = serverMs - read.receivedAt
      }
      return { journey, running: read.body, readFailed: false }
    }
    failed(read.failure)
    return { journey, running: previousRows.get(journey.journeyId)?.running ?? null, readFailed: true }
  })

  const status = failedReads === 0 ? 'ok' : networkFailures === attempted ? 'lost' : 'partial'
  const readAt = failedReads < attempted ? now : (previous?.readAt ?? null)
  return { today, stations, rows, hasTimetable, status, failedReads, clockOffsetMs, journeysByDate, readAt, lastFailure }
}

/** The corrected time: the device clock plus the offset the server last reported. */
export function correctedNow(offsetMs: number | null, deviceMs: number): number {
  return deviceMs + (offsetMs ?? 0)
}
