import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createPoller } from './poller.ts'

test('refreshes requested during a read wait for it to finish instead of overlapping', async () => {
  let inFlight = 0
  let peak = 0
  let calls = 0
  let releaseFirst: (() => void) | undefined
  let onSecond: (() => void) | undefined
  const firstRead = new Promise<void>((resolve) => { releaseFirst = resolve })
  const secondRead = new Promise<void>((resolve) => { onSecond = resolve })

  const poller = createPoller({
    intervalMs: 60_000,
    run: async () => {
      calls += 1
      inFlight += 1
      peak = Math.max(peak, inFlight)
      if (calls === 1) await firstRead
      inFlight -= 1
      if (calls === 2) onSecond?.()
    },
  })

  try {
    poller.start()
    poller.refreshNow()
    poller.refreshNow()
    await new Promise((resolve) => setTimeout(resolve, 0))
    assert.equal(calls, 1)
    releaseFirst?.()
    await Promise.race([secondRead, new Promise((_, reject) => setTimeout(() => reject(new Error('second read did not start')), 500))])
    assert.equal(calls, 2)
    assert.equal(peak, 1)
  } finally {
    poller.stop()
  }
})

test('stopping aborts an in-flight read so a superseded view can ignore its answer', async () => {
  let signal: AbortSignal | undefined
  let finish: (() => void) | undefined
  const pending = new Promise<void>((resolve) => { finish = resolve })
  let completed = false
  const poller = createPoller({
    intervalMs: 60_000,
    run: async (nextSignal) => {
      signal = nextSignal
      await pending
      if (!nextSignal.aborted) completed = true
    },
  })

  poller.start()
  await new Promise((resolve) => setTimeout(resolve, 0))
  poller.stop()
  finish?.()
  await new Promise((resolve) => setTimeout(resolve, 0))

  assert.equal(signal?.aborted, true)
  assert.equal(completed, false)
})
