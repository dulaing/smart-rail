import type { Fetcher } from './api.ts'
import type { ChangeConfirmed, ChangePreview, ChangeRequestBody, Incident } from './types.ts'

// The three staff calls of the change flow: preview, confirm and the list of open incidents.
// Each answer is sorted into one of a few kinds so the screen can tell "refused" from "could not
// be sent" from "sent but the answer is unknown". Nothing in this file ever sends a second request.

export type CallResult<T> =
  | { kind: 'ok'; body: T }
  /** The API understood the request and said no: 400, 404, 409 or another 4xx. Nothing was written. */
  | {
      kind: 'rejected'
      status: number
      title: string
      detail: string
      code: string | null
      latestVersion: number | null
    }
  /** 401 (sign in again) or 403 (not allowed). Nothing was written. */
  | { kind: 'denied'; status: 401 | 403 }
  /** No answer: the network failed or the wait ran out. A preview was harmless; a confirmation may or may not have happened. */
  | { kind: 'no-answer'; reason: 'network' | 'timeout' }
  /** A 5xx, or a success whose body could not be read. The server may or may not have written. */
  | { kind: 'server-error'; status: number }

/** Long enough for a busy API, short enough that a person is not left waiting on a lost response. */
export const callTimeoutMs = 20_000

/**
 * Builds the function that calls a staff route: it adds the access token and gives up after
 * `timeoutMs`. If no token can be had the call is reported as 401, as the API would.
 */
export function staffFetcher(
  apiUrl: string,
  getToken: () => Promise<string | null>,
  base: Fetcher = (url, init) => fetch(url, init),
  timeoutMs = callTimeoutMs,
): Fetcher {
  return async (path, init = {}) => {
    let token: string | null
    try {
      token = await getToken()
    } catch {
      token = null
    }
    if (!token) return new Response(null, { status: 401 })
    return base(`${apiUrl}${path}`, {
      ...init,
      signal: init.signal ?? AbortSignal.timeout(timeoutMs),
      headers: { ...init.headers, Authorization: `Bearer ${token}` },
    })
  }
}

async function send<T>(call: Fetcher, path: string, init: RequestInit): Promise<CallResult<T>> {
  let response: Response
  try {
    response = await call(path, init)
  } catch (error) {
    const timedOut = error instanceof DOMException && (error.name === 'TimeoutError' || error.name === 'AbortError')
    return { kind: 'no-answer', reason: timedOut ? 'timeout' : 'network' }
  }

  if (response.ok) {
    try {
      return { kind: 'ok', body: (await response.json()) as T }
    } catch {
      return { kind: 'server-error', status: response.status }
    }
  }
  if (response.status === 401 || response.status === 403) return { kind: 'denied', status: response.status }
  if (response.status >= 500) return { kind: 'server-error', status: response.status }

  let problem: { title?: unknown; detail?: unknown; code?: unknown; latestVersion?: unknown } = {}
  try {
    problem = (await response.json()) as typeof problem
  } catch {
    // A refusal without a readable body still counts as a refusal.
  }
  return {
    kind: 'rejected',
    status: response.status,
    title: typeof problem.title === 'string' ? problem.title : 'The request was refused',
    detail: typeof problem.detail === 'string' ? problem.detail : `The API answered ${response.status}.`,
    code: typeof problem.code === 'string' ? problem.code : null,
    latestVersion: typeof problem.latestVersion === 'number' ? problem.latestVersion : null,
  }
}

const post = (body: unknown): RequestInit => ({
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
})

/** Shows what a change would do. Nothing is stored, versioned or sent. */
export function previewChange(call: Fetcher, journeyId: string, request: ChangeRequestBody) {
  return send<ChangePreview>(call, `/api/operations/journeys/${journeyId}/changes/preview`, post(request))
}

/** Commits a previewed change. `request.previewedVersion` is the version the preview returned. */
export function confirmChange(call: Fetcher, journeyId: string, request: ChangeRequestBody) {
  return send<ChangeConfirmed>(call, `/api/operations/journeys/${journeyId}/changes`, post(request))
}

/** Open incidents a change may name. Only asked for by someone holding incidents.manage. */
export async function openIncidents(call: Fetcher): Promise<CallResult<Incident[]>> {
  const result = await send<{ items: Incident[] }>(call, '/api/operations/incidents?state=OPEN', { method: 'GET' })
  return result.kind === 'ok' ? { kind: 'ok', body: result.body.items } : result
}
