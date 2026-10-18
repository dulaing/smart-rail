// How the workspaces talk to the API. Nothing here knows about React, Keycloak or the DOM, so
// the same code runs in the node tests with a fake fetch.

export type Fetcher = (url: string, init?: RequestInit) => Promise<Response>

/** Why a read gave no usable answer. A network failure is never reported as a permission or HTTP error. */
export type ReadFailure =
  | { type: 'network' }
  | { type: 'http'; status: number; detail: string | null }
  | { type: 'unreadable' }

export type ReadResult<T> =
  | {
      kind: 'ok'
      body: T
      /** True when the server answered 304 and the body is the copy kept from an earlier read. */
      notModified: boolean
      /** X-SmartRail-Server-Time, when the response carried it. */
      serverTime: string | null
      /** Device time when the response arrived, for correcting the clock. */
      receivedAt: number
    }
  | { kind: 'failed'; failure: ReadFailure }

export type Reader = {
  read<T>(path: string, options?: { signal?: AbortSignal; fresh?: boolean }): Promise<ReadResult<T>>
}

const serverTimeHeader = 'X-SmartRail-Server-Time'

/**
 * Reads public JSON with ETags: the last body for each path is kept, sent back as If-None-Match,
 * and reused when the server answers 304. `fresh` skips that and asks for a full answer, which is
 * what a person needs after a write whose outcome is unknown.
 */
export function createReader(fetcher: Fetcher, apiUrl: string): Reader {
  const kept = new Map<string, { etag: string; body: unknown }>()

  return {
    async read<T>(path: string, options: { signal?: AbortSignal; fresh?: boolean } = {}): Promise<ReadResult<T>> {
      const earlier = options.fresh ? undefined : kept.get(path)
      let response: Response
      try {
        response = await fetcher(`${apiUrl}${path}`, {
          signal: options.signal,
          headers: earlier ? { 'If-None-Match': earlier.etag } : undefined,
        })
      } catch {
        return { kind: 'failed', failure: { type: 'network' } }
      }

      const receivedAt = Date.now()
      const serverTime = response.headers.get(serverTimeHeader)

      if (response.status === 304) {
        // Without a kept copy a 304 means nothing, so treat it as a failed read.
        return earlier
          ? { kind: 'ok', body: earlier.body as T, notModified: true, serverTime, receivedAt }
          : { kind: 'failed', failure: { type: 'http', status: 304, detail: null } }
      }
      if (!response.ok) {
        return { kind: 'failed', failure: { type: 'http', status: response.status, detail: await detailOf(response) } }
      }

      let body: T
      try {
        body = (await response.json()) as T
      } catch {
        return { kind: 'failed', failure: { type: 'unreadable' } }
      }
      const etag = response.headers.get('ETag')
      if (etag) kept.set(path, { etag, body })
      return { kind: 'ok', body, notModified: false, serverTime, receivedAt }
    },
  }
}

/** The detail line of an RFC 7807 problem response, if the body is one. */
export async function detailOf(response: Response): Promise<string | null> {
  try {
    const problem = (await response.json()) as { detail?: unknown }
    return typeof problem.detail === 'string' ? problem.detail : null
  } catch {
    return null
  }
}

/**
 * Runs `work` over `items` with at most `limit` running at once, and returns the results in the
 * order of `items`. This bounds how many requests one refresh can have in flight.
 */
export async function mapLimited<T, R>(items: readonly T[], limit: number, work: (item: T) => Promise<R>): Promise<R[]> {
  const results = new Array<R>(items.length)
  let next = 0
  const lane = async () => {
    while (next < items.length) {
      const index = next++
      results[index] = await work(items[index])
    }
  }
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, lane))
  return results
}
