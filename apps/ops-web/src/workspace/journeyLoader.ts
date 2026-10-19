import type { ReadFailure, Reader } from './api.ts'
import type { ConfirmedState, RunningResponse, Station } from './types.ts'

// One refresh of the journey workspace: the running picture (schedule, estimates, confirmed
// platforms) and the confirmed state (changes in force and the whole-journey version).

export type JourneySnapshot = {
  stations: Station[]
  running: RunningResponse | null
  changes: ConfirmedState | null
  /** The failure of the latest attempt at each read, null when it worked. */
  runningFailure: ReadFailure | null
  changesFailure: ReadFailure | null
  clockOffsetMs: number | null
  /** When something was last read successfully, on the corrected clock. */
  readAt: number | null
}

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export const isJourneyId = (value: string): boolean => guid.test(value)

export async function loadJourney(options: {
  reader: Reader
  journeyId: string
  now: number
  previous: JourneySnapshot | null
  signal?: AbortSignal
  /** Ask for full answers instead of 304s, as when checking what happened to a lost write. */
  fresh?: boolean
}): Promise<JourneySnapshot> {
  const { reader, journeyId, now, previous, signal, fresh } = options
  const [stationsRead, runningRead, changesRead] = await Promise.all([
    reader.read<Station[]>('/api/stations', { signal, fresh }),
    reader.read<RunningResponse>(`/api/journeys/${journeyId}/running`, { signal, fresh }),
    reader.read<ConfirmedState>(`/api/journeys/${journeyId}/changes`, { signal, fresh }),
  ])

  let clockOffsetMs = previous?.clockOffsetMs ?? null
  if (runningRead.kind === 'ok' && runningRead.serverTime) {
    const serverMs = Date.parse(runningRead.serverTime)
    if (!Number.isNaN(serverMs)) clockOffsetMs = serverMs - runningRead.receivedAt
  }

  const anyOk = [stationsRead, runningRead, changesRead].some((r) => r.kind === 'ok')
  return {
    stations: stationsRead.kind === 'ok' ? stationsRead.body : (previous?.stations ?? []),
    running: runningRead.kind === 'ok' ? runningRead.body : (previous?.running ?? null),
    changes: changesRead.kind === 'ok' ? changesRead.body : (previous?.changes ?? null),
    runningFailure: runningRead.kind === 'failed' ? runningRead.failure : null,
    changesFailure: changesRead.kind === 'failed' ? changesRead.failure : null,
    clockOffsetMs,
    readAt: anyOk ? now : (previous?.readAt ?? null),
  }
}
