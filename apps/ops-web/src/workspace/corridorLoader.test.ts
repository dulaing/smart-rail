// Run with: npm test  (node --test). These run the real loader against a fake API, so they show
// how the overview handles dates, limits and failures; they do not show a rendered screen.
import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createReader } from './api.ts'
import { loadCorridor, maxParallelReads } from './corridorLoader.ts'
import { delayOf, evidenceOf } from './facts.ts'
import { confirmedState, fakeFetch, journey, json, running, stations, type Answer, type Call } from './testkit.ts'
import type { ScheduledJourney } from './types.ts'

const at = (iso: string) => Date.parse(iso)

/** A fake API serving these journeys by service date, with a running answer per journey. */
function api(options: {
  byDate: Record<string, ScheduledJourney[]>
  observedAt?: string
  onRunning?: (call: Call) => Answer | Promise<Answer>
}) {
  return fakeFetch((call) => {
    if (call.path === '/api/stations') return json(stations)
    const date = /serviceDate=([\d-]+)/.exec(call.path)?.[1]
    if (date) return json({ serviceDate: date, scheduleNote: '', journeys: options.byDate[date] ?? [] })
    const id = /journeys\/([^/]+)\/running/.exec(call.path)?.[1]
    if (id) {
      if (options.onRunning) return options.onRunning(call)
      const j = Object.values(options.byDate).flat().find((x) => x.journeyId === id)!
      return json(running(j, options.observedAt ?? null))
    }
    return json(confirmedState('x', 1))
  })
}

test('an overnight train from yesterday is listed after midnight, a finished one is not', async () => {
  const overnight = journey('night', 'SIM-9001', '2026-10-18', '23:45')
  overnight.stops[2].scheduledArrival = '2026-10-19T00:45:00+05:30'
  const evening = journey('evening', 'SIM-2001', '2026-10-18', '17:10', '18:30')
  const todays = journey('today', 'SIM-1001', '2026-10-19')
  const { fetcher, calls } = api({ byDate: { '2026-10-18': [overnight, evening], '2026-10-19': [todays] } })

  const snapshot = await loadCorridor({
    reader: createReader(fetcher, ''),
    now: at('2026-10-19T00:30:00+05:30'),
    previous: null,
    stationIds: null,
  })

  assert.equal(snapshot.today, '2026-10-19')
  assert.deepEqual(snapshot.rows.map((r) => r.journey.trainNumber).sort(), ['SIM-1001', 'SIM-9001'])
  // Only the evening train's running state was never asked for.
  assert.equal(calls.some((c) => c.path.includes('/evening/running')), false)
  assert.equal(snapshot.status, 'ok')
})

test('the service date comes from Sri Lanka time, not from the UTC date', async () => {
  // 18:45 UTC on the 18th is 00:15 on the 19th in Sri Lanka.
  const { fetcher, calls } = api({ byDate: {} })
  const snapshot = await loadCorridor({
    reader: createReader(fetcher, ''),
    now: at('2026-10-18T18:45:00Z'),
    previous: null,
    stationIds: null,
  })
  assert.equal(snapshot.today, '2026-10-19')
  assert.deepEqual(
    calls.filter((c) => c.path.includes('serviceDate')).map((c) => c.path).sort(),
    ['/api/journeys?serviceDate=2026-10-18', '/api/journeys?serviceDate=2026-10-19'],
  )
  // A successful, empty answer is not an error.
  assert.equal(snapshot.hasTimetable, true)
  assert.equal(snapshot.rows.length, 0)
  assert.equal(snapshot.status, 'ok')
})

test('one refresh never has more than the limit of running reads in flight', async () => {
  const many = Array.from({ length: 12 }, (_, i) => journey(`j${i}`, `SIM-${i}`, '2026-10-18'))
  let inFlight = 0
  let peak = 0
  const { fetcher } = api({
    byDate: { '2026-10-18': many },
    onRunning: async (call) => {
      inFlight += 1
      peak = Math.max(peak, inFlight)
      await new Promise((resolve) => setTimeout(resolve, 5))
      inFlight -= 1
      const id = /journeys\/([^/]+)\/running/.exec(call.path)![1]
      return json(running(many.find((j) => j.journeyId === id)!, null))
    },
  })

  const snapshot = await loadCorridor({ reader: createReader(fetcher, ''), now: at('2026-10-18T09:00:00+05:30'), previous: null, stationIds: null })

  assert.equal(snapshot.rows.length, 12)
  assert.ok(peak > 1, 'reads should run in parallel');
  assert.ok(peak <= maxParallelReads, `peak was ${peak}`)
})

test('a failed read keeps that row’s earlier reading and marks it; the others stay current', async () => {
  const a = journey('a', 'SIM-A', '2026-10-18')
  const b = journey('b', 'SIM-B', '2026-10-18')
  const now = at('2026-10-18T09:00:00+05:30')
  const first = api({ byDate: { '2026-10-18': [a, b] }, observedAt: '2026-10-18T08:59:30+05:30' })
  const reader = createReader(first.fetcher, '')
  const before = await loadCorridor({ reader, now, previous: null, stationIds: null })

  const second = api({
    byDate: { '2026-10-18': [a, b] },
    observedAt: '2026-10-18T09:00:10+05:30',
    onRunning: (call) =>
      call.path.includes('/a/running') ? new Response('boom', { status: 500 }) : json(running(b, '2026-10-18T09:00:10+05:30')),
  })
  const after = await loadCorridor({ reader: createReader(second.fetcher, ''), now: now + 15_000, previous: before, stationIds: null })

  const rowA = after.rows.find((r) => r.journey.journeyId === 'a')!
  const rowB = after.rows.find((r) => r.journey.journeyId === 'b')!
  assert.equal(after.status, 'partial')
  assert.equal(after.failedReads, 1)
  assert.equal(rowA.readFailed, true)
  assert.equal(rowA.running?.lastObservation?.observedAt, '2026-10-18T08:59:30+05:30', 'keeps the earlier reading')
  assert.equal(rowB.readFailed, false)
  assert.equal(rowB.running?.lastObservation?.observedAt, '2026-10-18T09:00:10+05:30')
})

test('losing the whole connection keeps the last data and says so; first load says nothing was read', async () => {
  const a = journey('a', 'SIM-A', '2026-10-18')
  const now = at('2026-10-18T09:00:00+05:30')
  const up = api({ byDate: { '2026-10-18': [a] }, observedAt: '2026-10-18T08:59:30+05:30' })
  const before = await loadCorridor({ reader: createReader(up.fetcher, ''), now, previous: null, stationIds: null })

  const down = fakeFetch(() => 'lost')
  const lost = await loadCorridor({ reader: createReader(down.fetcher, ''), now: now + 15_000, previous: before, stationIds: null })
  assert.equal(lost.status, 'lost')
  assert.equal(lost.hasTimetable, true)
  assert.equal(lost.rows.length, 1)
  assert.equal(lost.rows[0].running?.trainNumber, 'SIM-A')
  assert.equal(lost.readAt, before.readAt, 'last successful read time is kept')

  const never = await loadCorridor({ reader: createReader(down.fetcher, ''), now, previous: null, stationIds: null })
  assert.equal(never.hasTimetable, false, 'cannot tell "no trains" from "could not read"')
  assert.equal(never.status, 'lost')
})

test('a 304 reuses the body, and the evidence still ages from the observation time', async () => {
  const a = journey('a', 'SIM-A', '2026-10-18')
  const observedAt = '2026-10-18T08:59:30+05:30'
  const t0 = at('2026-10-18T09:00:00+05:30')
  let runningCalls = 0
  const { fetcher, calls } = fakeFetch((call) => {
    if (call.path === '/api/stations') return json(stations)
    if (call.path.includes('serviceDate=2026-10-18')) return json({ serviceDate: '2026-10-18', scheduleNote: '', journeys: [a] })
    if (call.path.includes('serviceDate')) return json({ serviceDate: 'x', scheduleNote: '', journeys: [] })
    runningCalls += 1
    // The second time, the client holds the current version, so the server answers 304.
    return call.headers['if-none-match'] === '"v1"'
      ? new Response(null, { status: 304, headers: { ETag: '"v1"' } })
      : json(running(a, observedAt), 200, { ETag: '"v1"' })
  })
  const reader = createReader(fetcher, '')

  const first = await loadCorridor({ reader, now: t0, previous: null, stationIds: null })
  const second = await loadCorridor({ reader, now: t0 + 15_000, previous: first, stationIds: null })

  assert.equal(runningCalls, 2)
  assert.equal(calls.filter((c) => c.path.includes('running'))[1].headers['if-none-match'], '"v1"')
  const row = second.rows[0]
  assert.equal(row.readFailed, false)
  assert.equal(evidenceOf(row.running, t0), 'CURRENT')
  // 200 seconds later, with no new answer, the same cached body is ageing, then unknown.
  assert.equal(evidenceOf(row.running, t0 + 200_000), 'AGEING')
  assert.equal(evidenceOf(row.running, t0 + 215_000), 'UNKNOWN')
  assert.deepEqual(delayOf(row.running, 'UNKNOWN'), { kind: 'unknown' }, 'an old estimate is never on time')
  assert.deepEqual(delayOf(row.running, 'CURRENT'), { kind: 'late', minutes: 4 })
})

test('station staff are listed only the journeys that stop at their station', async () => {
  const a = journey('a', 'SIM-A', '2026-10-18')
  const b = journey('b', 'SIM-B', '2026-10-18')
  b.stops = b.stops.filter((s) => s.stationId !== 'st-rgm')
  const { fetcher } = api({ byDate: { '2026-10-18': [a, b] } })

  const snapshot = await loadCorridor({
    reader: createReader(fetcher, ''),
    now: at('2026-10-18T09:00:00+05:30'),
    previous: null,
    stationIds: new Set(['st-rgm']),
  })
  assert.deepEqual(snapshot.rows.map((r) => r.journey.trainNumber), ['SIM-A'])
})
