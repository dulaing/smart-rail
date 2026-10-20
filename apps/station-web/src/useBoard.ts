import { useEffect, useRef, useState } from 'react'
import { FeedClient } from './api.ts'
import { refresh, type FeedData } from './feed.ts'
import { emptySnapshot, loadSnapshot, saveSnapshot } from './snapshot.ts'

const apiUrl = import.meta.env.VITE_API_URL ?? 'https://localhost:7074'

/** Boards poll every 15 seconds, one refresh at a time. */
const pollMs = 15_000

export type BoardData = FeedData & {
  /** False until the first refresh has finished, so a first boot can say "loading". */
  refreshed: boolean
  /** The browser is back (or the page is visible again) and live information is being asked for again. */
  reconnecting: boolean
  /** When live information was last refreshed successfully (device time), else null. */
  refreshedAt: number | null
  /** Live information was lost and has returned since the page opened. */
  recovered: boolean
}

function initial(): BoardData {
  // The saved timetable is shown at once, as timetable only, while the first refresh runs.
  const snapshot = loadSnapshot(window.localStorage) ?? emptySnapshot()
  return { snapshot, live: new Map(), feed: 'connecting', partial: false, offsetMs: 0, refreshed: false, reconnecting: false, refreshedAt: null, recovered: false }
}

/**
 * Keeps the board's data fresh: refreshes now, every 15 seconds, when the browser reports it is
 * back online and when the page becomes visible. A refresh never overlaps another, and the
 * answer to a superseded one is dropped.
 */
export function useBoard(stationReference: string | null): BoardData {
  const [data, setData] = useState<BoardData>(initial)
  const latest = useRef(data)

  useEffect(() => {
    if (stationReference === null) return
    const controller = new AbortController()
    const client = new FeedClient(apiUrl)
    let timer: number | undefined
    let busy = false

    const run = async (askedForAgain = false) => {
      if (busy || controller.signal.aborted || document.hidden) return
      busy = true
      try {
        const before = latest.current
        // Asking again after live information was lost shows "refreshing" until the answer comes.
        if (askedForAgain && before.feed === 'unavailable') {
          latest.current = { ...before, reconnecting: true }
          setData(latest.current)
        }
        const next = await refresh(client, before, stationReference, Date.now, controller.signal)
        if (controller.signal.aborted) return
        latest.current = {
          ...next,
          refreshed: true,
          reconnecting: false,
          refreshedAt: next.feed === 'live' ? Date.now() : before.refreshedAt,
          recovered: before.recovered || (before.feed === 'unavailable' && next.feed === 'live'),
        }
        saveSnapshot(window.localStorage, next.snapshot)
        setData(latest.current)
      } finally {
        busy = false
      }
    }
    const schedule = () => {
      window.clearTimeout(timer)
      timer = window.setTimeout(() => void run().then(schedule), pollMs)
    }
    const runNow = () => {
      window.clearTimeout(timer)
      void run(true).then(schedule)
    }

    runNow()
    window.addEventListener('online', runNow)
    document.addEventListener('visibilitychange', runNow)
    return () => {
      controller.abort()
      window.clearTimeout(timer)
      window.removeEventListener('online', runNow)
      document.removeEventListener('visibilitychange', runNow)
    }
  }, [stationReference])

  return data
}

/** The current time, corrected with the server clock, redrawn every second so evidence keeps ageing between polls. */
export function useNow(offsetMs: number): number {
  const [tick, setTick] = useState(() => Date.now())
  useEffect(() => {
    const timer = window.setInterval(() => setTick(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [])
  return tick + offsetMs
}
