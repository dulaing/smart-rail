// Repeats a read on a timer without ever overlapping itself. The next run is scheduled only after
// the current one has finished, so a slow API cannot pile up requests. Stopping aborts the run in
// progress, and `run` must ignore its own result once its signal is aborted: that is how an
// answer for a superseded view is kept from replacing the current one.

export type Poller = {
  start(): void
  stop(): void
  /** Run again as soon as possible: now when idle, right after the current run when busy. */
  refreshNow(): void
}

export function createPoller(options: {
  run: (signal: AbortSignal) => Promise<void>
  intervalMs: number
  /** While true the timer keeps ticking but no request is made. */
  isHidden?: () => boolean
}): Poller {
  const { run, intervalMs, isHidden = () => false } = options
  let timer: ReturnType<typeof setTimeout> | undefined
  let controller: AbortController | undefined
  let running = false
  let again = false
  let stopped = true

  const schedule = () => {
    clearTimeout(timer)
    if (!stopped) timer = setTimeout(tick, intervalMs)
  }

  const tick = async () => {
    if (stopped) return
    if (running) {
      again = true
      return
    }
    if (isHidden()) {
      schedule()
      return
    }
    running = true
    const mine = new AbortController()
    controller = mine
    try {
      await run(mine.signal)
    } catch {
      // `run` reports its own failures; a throw must not stop the polling.
    } finally {
      running = false
    }
    if (stopped) return
    // Started again while the aborted run was still ending: run once more straight away.
    if (again || mine.signal.aborted) {
      again = false
      void tick()
    } else {
      schedule()
    }
  }

  return {
    start() {
      if (!stopped) return
      stopped = false
      void tick()
    },
    stop() {
      stopped = true
      again = false
      clearTimeout(timer)
      controller?.abort()
    },
    refreshNow() {
      if (stopped) return
      if (running) {
        again = true
        return
      }
      clearTimeout(timer)
      void tick()
    },
  }
}
