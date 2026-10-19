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
}

function initial(): BoardData {
  // The saved timetable is shown at once, as timetable only, while the first refresh runs.
  const snapshot = loadSnapshot(window.localStorage) ?? emptySnapshot()
  return { snapshot, live: new Map(), feed: 'connecting', partial: false, offsetMs: 0, refreshed: false }
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

    const run = async () => {
      if (busy || controller.signal.aborted || document.hidden) return
      busy = true
      try {
        const next = await refresh(client, latest.current, stationReference, Date.now, controller.signal)
        if (controller.signal.aborted) return
        latest.current = { ...next, refreshed: true }
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
      void run().then(schedule)
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
