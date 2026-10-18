// The public reads the board uses, and a small client for them. All of them are anonymous: the
// board never signs in and never calls Keycloak. Shapes follow Rail/RailEndpoints.cs,
// Rail/Running/RunningResponse.cs and Operations/Changes/PublicChangesEndpoint.cs.

export type Station = { id: string; code: string; nameEn: string; nameSi: string; nameTa: string }

export type JourneyStop = {
  stopId: string
  sequence: number
  stationId: string
  stationCode: string
  scheduledArrival: string | null
  scheduledDeparture: string | null
  plannedPlatform: string | null
}

export type Journey = {
  journeyId: string
  serviceDate: string
  trainNumber: string
  sourceKind: string
  sourceLabel: string
  sourceUrl: string | null
  sourceCheckedAt: string | null
  stops: JourneyStop[]
}

export type JourneysResponse = { serviceDate: string; scheduleNote: string; journeys: Journey[] }

export type RunningStop = {
  stopId: string
  stationId: string
  sequence: number
  scheduledArrival: string | null
  scheduledDeparture: string | null
  plannedPlatform: string | null
  estimatedArrival: string | null
  estimatedDeparture: string | null
  served: boolean
  confirmedPlatform: string | null
  confirmedPlatformAt: string | null
  /** The confirmed platform if there is one, else the planned one. */
  platform: string | null
}

export type RunningResponse = {
  journeyId: string
  thresholds: { ageingAfterSeconds: number; unknownAfterSeconds: number }
  freshness: 'CURRENT' | 'AGEING' | 'UNKNOWN'
  lastObservation: {
    observedAt: string
    fromStopId: string
    toStopId: string
    progressPermille: number
  } | null
  availability: 'AS_SCHEDULED' | 'ENDS_EARLY' | 'CANCELLED'
  confirmedDelayMinutes: number | null
  finalStopId: string | null
  stops: RunningStop[]
}

export type ActiveChange = {
  changeId: string
  kind: 'DELAY' | 'CANCEL' | 'PLATFORM' | 'EARLY_END'
  confirmedAt: string
  delayMinutes: number | null
  newPlatform: string | null
  targetStopId: string | null
  finalStopId: string | null
}

export type ChangesResponse = { journeyId: string; journeyVersion: number; activeChanges: ActiveChange[] }

export type Fetched<T> = {
  data: T
  /** True when the server answered 304 and the saved copy of the body was reused. */
  notModified: boolean
  /** The server clock from X-SmartRail-Server-Time (running reads only), else null. */
  serverTimeMs: number | null
}

export class ApiError extends Error {
  status: number
  constructor(status: number) {
    super(`The API answered ${status}`)
    this.status = status
  }
}

const requestTimeoutMs = 10_000

/**
 * Reads JSON with one ETag per URL. A 304 gives back the body saved from the last 200, so the
 * caller gets the same shape either way and can keep ageing its evidence between polls.
 */
export class FeedClient {
  private base: string
  private saved = new Map<string, { etag: string; data: unknown }>()

  constructor(base: string) {
    this.base = base.replace(/\/$/, '')
  }

  async get<T>(path: string, signal: AbortSignal): Promise<Fetched<T>> {
    const known = this.saved.get(path)
    const response = await fetch(`${this.base}${path}`, {
      headers: known ? { 'If-None-Match': known.etag } : {},
      // The app owns revalidation, so the browser's own HTTP cache must not answer for it.
      cache: 'no-store',
      signal: AbortSignal.any([signal, AbortSignal.timeout(requestTimeoutMs)]),
    })
    const serverTime = response.headers.get('X-SmartRail-Server-Time')
    const serverTimeMs = serverTime ? Date.parse(serverTime) : Number.NaN

    if (response.status === 304 && known) {
      return { data: known.data as T, notModified: true, serverTimeMs: Number.isNaN(serverTimeMs) ? null : serverTimeMs }
    }
    if (!response.ok) throw new ApiError(response.status)

    const data = (await response.json()) as T
    const etag = response.headers.get('ETag')
    if (etag) this.saved.set(path, { etag, data })
    return { data, notModified: false, serverTimeMs: Number.isNaN(serverTimeMs) ? null : serverTimeMs }
  }
}

/** Runs the work with at most `limit` calls in flight, keeping the results in input order. */
export async function mapLimit<T, R>(items: readonly T[], limit: number, work: (item: T) => Promise<R>): Promise<R[]> {
  const results = new Array<R>(items.length)
  let next = 0
  const runner = async () => {
    while (next < items.length) {
      const index = next++
      results[index] = await work(items[index])
    }
  }
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, runner))
  return results
}
