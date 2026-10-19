import { useCallback, useEffect, useRef, useState } from 'react'
import { createPoller, type Poller } from './poller.ts'

/** The staff screens poll every 15 seconds (scope decision: web boards and Operations). */
export const pollIntervalMs = 15_000

/**
 * Loads data now and then every 15 seconds while the page is visible, and again straight away
 * when the page becomes visible or the browser comes back online. `key` names what is being
 * viewed: when it changes the old poller stops, an answer still on its way is ignored, and the
 * old data is not returned for the new view. `isStale` rejects an answer that was computed for a
 * moment that has passed (the service date rolled over) and asks for a new one instead.
 */
export function usePolled<T>(
  key: string,
  load: (signal: AbortSignal, previous: T | null) => Promise<T>,
  isStale: (data: T) => boolean = () => false,
): { data: T | null; refresh: () => void } {
  const [state, setState] = useState<{ key: string; value: T } | null>(null)
  const loadRef = useRef(load)
  const staleRef = useRef(isStale)
  const pollerRef = useRef<Poller | null>(null)

  useEffect(() => {
    loadRef.current = load
    staleRef.current = isStale
  })

  useEffect(() => {
    let latest: T | null = null
    const poller = createPoller({
      intervalMs: pollIntervalMs,
      isHidden: () => document.hidden,
      run: async (signal) => {
        const result = await loadRef.current(signal, latest)
        if (signal.aborted) return
        if (staleRef.current(result)) {
          poller.refreshNow()
          return
        }
        latest = result
        setState({ key, value: result })
      },
    })
    const wake = () => {
      if (!document.hidden) poller.refreshNow()
    }
    document.addEventListener('visibilitychange', wake)
    window.addEventListener('online', wake)
    pollerRef.current = poller
    poller.start()
    return () => {
      document.removeEventListener('visibilitychange', wake)
      window.removeEventListener('online', wake)
      poller.stop()
      pollerRef.current = null
    }
  }, [key])

  const refresh = useCallback(() => pollerRef.current?.refreshNow(), [])
  return { data: state && state.key === key ? state.value : null, refresh }
}

/**
 * The server-corrected current time, re-read every few seconds so that evidence keeps ageing
 * between polls, after a 304 and while the connection is down.
 */
export function useNow(offsetMs: number | null, tickMs = 5_000): number {
  const [device, setDevice] = useState(() => Date.now())
  useEffect(() => {
    const tick = () => setDevice(Date.now())
    const id = setInterval(tick, tickMs)
    document.addEventListener('visibilitychange', tick)
    return () => {
      clearInterval(id)
      document.removeEventListener('visibilitychange', tick)
    }
  }, [tickMs])
  return device + (offsetMs ?? 0)
}
