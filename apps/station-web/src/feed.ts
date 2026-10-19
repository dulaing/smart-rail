// One refresh of everything a board needs, with no React and no timers so tests can drive it with
// a fake source. useBoard.ts calls it every 15 seconds.
//
// The two kinds of failure are kept apart on purpose:
//  - some trains cannot be refreshed: the board keeps what it has, and only those rows say so;
//  - nothing live can be reached: the board is "unavailable", every row is timetable only and the
//    running and change data is dropped so none of it can look current when the link returns.

import { liveCandidates, type JourneyLive } from './board.ts'
import { findStation } from './config.ts'
import { mapLimit, type ChangesResponse, type Fetched, type JourneysResponse, type RunningResponse, type Station } from './api.ts'
import { pruneSnapshot, type Snapshot } from './snapshot.ts'
import { journeysOf } from './snapshot.ts'
import { serviceDatesAround } from './time.ts'

export type FeedState = 'connecting' | 'live' | 'unavailable'

export type FeedData = {
  snapshot: Snapshot
  live: ReadonlyMap<string, JourneyLive>
  feed: FeedState
  /** Some trains, or some service dates, could not be refreshed this time. */
  partial: boolean
  /** Add to the device clock to get the server's time. */
  offsetMs: number
}

export type FeedSource = {
  get<T>(path: string, signal: AbortSignal): Promise<Fetched<T>>
}

/** At most this many reads are in flight at once. */
export const concurrency = 4

export async function refresh(
  source: FeedSource,
  previous: FeedData,
  stationReference: string,
  deviceNow: () => number,
  signal: AbortSignal,
): Promise<FeedData> {
  let offsetMs = previous.offsetMs
  const now = () => deviceNow() + offsetMs
  const noteServerTime = (result: Fetched<unknown>) => {
    if (result.serverTimeMs !== null) offsetMs = result.serverTimeMs - deviceNow()
  }

  let snapshot = previous.snapshot
  let reachable = false
  let timetableFailed = false

  try {
    const stations = await source.get<Station[]>('/api/stations', signal)
    snapshot = { ...snapshot, stations: stations.data, stationsSavedAt: deviceNow() }
    reachable = true
  } catch {
    timetableFailed = true
  }

  // Timetables are small and answered with 304 when nothing changed, so every date is asked for
  // each time. Tomorrow is included so that the next service date is already saved at midnight.
  const dates = serviceDatesAround(now())
  const days = { ...snapshot.days }
  await mapLimit(dates, concurrency, async (date) => {
    try {
      const result = await source.get<JourneysResponse>(`/api/journeys?serviceDate=${date}`, signal)
      days[date] = { serviceDate: date, downloadedAt: deviceNow(), journeys: result.data.journeys }
      reachable = true
    } catch {
      timetableFailed = true
    }
  })
  snapshot = pruneSnapshot({ ...snapshot, days }, dates[0])

  if (signal.aborted) return previous
  if (!reachable) return { ...previous, snapshot, live: new Map(), feed: 'unavailable', partial: false, offsetMs }

  // Live reads for the trains that could matter. Without a valid station there is nothing to ask for.
  const station = findStation(snapshot.stations, stationReference)
  const candidates = station ? liveCandidates(journeysOf(snapshot), station.id, now()) : []
  const live = new Map<string, JourneyLive>()
  let runningOk = 0
  let liveFailed = false

  await mapLimit(candidates, concurrency, async (journey) => {
    const before = previous.live.get(journey.journeyId)
    let running = before?.running ?? null
    let changes = before?.changes ?? null
    let checkedAt = before?.checkedAt ?? null
    let failed = false

    try {
      const result = await source.get<RunningResponse>(`/api/journeys/${journey.journeyId}/running`, signal)
      noteServerTime(result)
      running = result.data
      checkedAt = now()
      runningOk++
    } catch {
      failed = true
    }
    try {
      changes = (await source.get<ChangesResponse>(`/api/journeys/${journey.journeyId}/changes`, signal)).data
    } catch {
      failed = true
    }
    liveFailed ||= failed
    live.set(journey.journeyId, { running, changes, refreshFailed: failed, checkedAt })
  })

  if (signal.aborted) return previous

  // Live information is lost as a whole when trains were asked for and none answered.
  if (candidates.length > 0 && runningOk === 0) {
    return { ...previous, snapshot, live: new Map(), feed: 'unavailable', partial: false, offsetMs }
  }
  return { snapshot, live, feed: 'live', partial: timetableFailed || liveFailed, offsetMs }
}
