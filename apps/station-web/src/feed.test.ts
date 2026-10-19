// Run with: npm test   (node --test src/*.test.ts)
import assert from 'node:assert/strict'
import { afterEach, test } from 'node:test'
import { ApiError, FeedClient, mapLimit, type Fetched } from './api.ts'
import { deriveRows } from './board.ts'
import { concurrency, refresh, type FeedData, type FeedSource } from './feed.ts'
import { at, changes, journey, running, stations } from './fixtures.ts'
import { emptySnapshot, journeysOf } from './snapshot.ts'

const fresh = (): FeedData => ({ snapshot: emptySnapshot(), live: new Map(), feed: 'connecting', partial: false, offsetMs: 0 })
const noSignal = new AbortController().signal

type Handler = (path: string) => unknown | Promise<unknown>

/** A source that answers from a table of handlers; a handler that throws is a failed read. */
function source(handlers: Record<string, Handler>, log: string[] = [], serverTime: number | null = null): FeedSource {
  return {
    async get<T>(path: string): Promise<Fetched<T>> {
      log.push(path)
      const key = Object.keys(handlers).find((k) => path.startsWith(k))
      if (!key) throw new ApiError(404)
      const data = await handlers[key](path)
      return { data: data as T, notModified: false, serverTimeMs: path.endsWith('/running') ? serverTime : null }
    },
  }
}

const day = (date: string, journeys = [journey()]) => ({ serviceDate: date, scheduleNote: '', journeys: date === '2026-10-15' ? journeys : [] })
const healthy = (log: string[] = [], serverTime: number | null = null) =>
  source(
    {
      '/api/stations': () => stations,
      '/api/journeys?serviceDate=': (path) => day(path.split('=')[1]),
      '/api/journeys/j-1001/running': () => running(),
      '/api/journeys/j-1001/changes': () => changes(),
    },
    log,
    serverTime,
  )

const device = (hhmm: string) => () => at(hhmm)

test('a complete refresh saves the timetable for yesterday, today and tomorrow and reads the nearby trains', async () => {
  const log: string[] = []
  const data = await refresh(healthy(log, at('07:30') + 2000), fresh(), 'GPH', device('07:30'), noSignal)
  assert.equal(data.feed, 'live')
  assert.equal(data.partial, false)
  assert.deepEqual(Object.keys(data.snapshot.days).sort(), ['2026-10-14', '2026-10-15', '2026-10-16'])
  assert.equal(data.snapshot.days['2026-10-15'].downloadedAt, at('07:30'))
  assert.equal(data.live.get('j-1001')?.refreshFailed, false)
  assert.ok(log.includes('/api/journeys/j-1001/running'))
  // The server clock was 2 seconds ahead of this device when the running read answered.
  assert.equal(data.offsetMs, 2000)
})

test('one train failing is a partial failure: the board stays live and keeps that train\'s earlier data', async () => {
  const before = await refresh(healthy(), fresh(), 'GPH', device('07:30'), noSignal)
  const other = journey('SIM-1003', 'j-1003')
  const handlers: Record<string, Handler> = {
    '/api/stations': () => stations,
    '/api/journeys?serviceDate=': (path: string) => day(path.split('=')[1], [journey(), other]),
    '/api/journeys/j-1001/running': () => running(),
    '/api/journeys/j-1001/changes': () => changes(),
    '/api/journeys/j-1003/running': () => {
      throw new ApiError(500)
    },
    '/api/journeys/j-1003/changes': () => changes([], 'j-1003'),
  }
  const data = await refresh(source(handlers), before, 'GPH', device('07:30'), noSignal)
  assert.equal(data.feed, 'live')
  assert.equal(data.partial, true)
  assert.equal(data.live.get('j-1001')?.refreshFailed, false)
  assert.equal(data.live.get('j-1003')?.refreshFailed, true)
  assert.equal(data.live.get('j-1003')?.running, null)

  // And a train that answered once keeps its earlier data, marked, when it later fails.
  handlers['/api/journeys/j-1003/running'] = () => running({ id: 'j-1003' })
  const answered = await refresh(source(handlers), data, 'GPH', device('07:30'), noSignal)
  handlers['/api/journeys/j-1003/running'] = () => {
    throw new ApiError(500)
  }
  const failedAgain = await refresh(source(handlers), answered, 'GPH', device('07:31'), noSignal)
  assert.equal(failedAgain.live.get('j-1003')?.refreshFailed, true)
  assert.ok(failedAgain.live.get('j-1003')?.running, 'the earlier running data is kept')
  assert.equal(failedAgain.live.get('j-1003')?.checkedAt, answered.live.get('j-1003')?.checkedAt)
})

test('losing every read is whole-feed loss: timetable stays, live data is dropped, and it recovers by itself', async () => {
  const before = await refresh(healthy(), fresh(), 'GPH', device('07:30'), noSignal)
  const down = source({
    '/api/stations': () => {
      throw new TypeError('Failed to fetch')
    },
    '/api/journeys': () => {
      throw new TypeError('Failed to fetch')
    },
  })
  const lost = await refresh(down, before, 'GPH', device('07:31'), noSignal)
  assert.equal(lost.feed, 'unavailable')
  assert.equal(lost.live.size, 0, 'no running or change data survives to look current')
  assert.deepEqual(lost.snapshot.days, before.snapshot.days)
  assert.deepEqual(lost.snapshot.stations, before.snapshot.stations)

  // The rows it can build are timetable only: no expected time, no confirmed platform.
  const rows = deriveRows({
    stationId: 's-gph',
    journeys: journeysOf(lost.snapshot),
    live: lost.live,
    now: at('07:31'),
    timetableOnly: true, // what the board does for any feed other than live
  })
  assert.equal(rows.length, 1)
  assert.equal(rows[0].timetableOnly, true)
  assert.equal(rows[0].expectedAt, null)

  const back = await refresh(healthy(), lost, 'GPH', device('07:32'), noSignal)
  assert.equal(back.feed, 'live')
  assert.equal(back.live.size, 1)
})

test('when trains are asked for and none answer, the live feed is lost even though the timetable arrived', async () => {
  const handlers = {
    '/api/stations': () => stations,
    '/api/journeys?serviceDate=': (path: string) => day(path.split('=')[1]),
    '/api/journeys/j-1001': () => {
      throw new ApiError(503)
    },
  }
  const data = await refresh(source(handlers), fresh(), 'GPH', device('07:30'), noSignal)
  assert.equal(data.feed, 'unavailable')
  assert.equal(Object.keys(data.snapshot.days).length, 3, 'the new timetable is still saved')
})

test('a station that does not exist asks for no live reads', async () => {
  const log: string[] = []
  const data = await refresh(healthy(log), fresh(), 'NOPE', device('07:30'), noSignal)
  assert.equal(data.feed, 'live')
  assert.ok(!log.some((p) => p.includes('/running')))
})

test('at most four reads are in flight at once', async () => {
  let inFlight = 0
  let peak = 0
  const many = Array.from({ length: 12 }, (_, i) => journey(`SIM-${i}`, `j-${i}`))
  const handlers = {
    '/api/stations': () => stations,
    '/api/journeys?serviceDate=': (path: string) => day(path.split('=')[1], many),
    '/api/journeys/': async () => {
      inFlight++
      peak = Math.max(peak, inFlight)
      await new Promise((resolve) => setTimeout(resolve, 5))
      inFlight--
      return running()
    },
  }
  await refresh(source(handlers), fresh(), 'GPH', device('07:30'), noSignal)
  assert.ok(peak > 1 && peak <= concurrency, `peak was ${peak}`)

  let direct = 0
  let directPeak = 0
  await mapLimit([1, 2, 3, 4, 5, 6, 7], 2, async () => {
    directPeak = Math.max(directPeak, ++direct)
    await new Promise((resolve) => setTimeout(resolve, 2))
    direct--
  })
  assert.equal(directPeak, 2)
})

test('Colombo midnight: the next date is already saved, the previous date is kept for overnight trains, older dates go', async () => {
  const overnight = journey('SIM-9001', 'j-9001', '2026-10-15', [
    ['n-1', 's-fot', null, '23:45', '6'],
    ['n-2', 's-rgm', '00:25+1', '00:26+1', '1'],
    ['n-3', 's-gph', '00:45+1', null, '2'],
  ])
  const log: string[] = []
  const handlers = {
    '/api/stations': () => stations,
    '/api/journeys?serviceDate=': (path: string) => ({ serviceDate: path.split('=')[1], scheduleNote: '', journeys: path.endsWith('2026-10-15') ? [overnight] : [] }),
    '/api/journeys/j-9001/running': () => running({ id: 'j-9001', stopSpecs: [['n-1', 's-fot', null, '23:45', '6'], ['n-2', 's-rgm', '00:25+1', '00:26+1', '1'], ['n-3', 's-gph', '00:45+1', null, '2']] }),
    '/api/journeys/j-9001/changes': () => changes([], 'j-9001'),
  }
  const evening = await refresh(source(handlers, log), fresh(), 'RGM', () => at('23:50'), noSignal)
  assert.deepEqual(Object.keys(evening.snapshot.days).sort(), ['2026-10-14', '2026-10-15', '2026-10-16'])

  // Twenty minutes after midnight it is the 16th: the 17th is fetched, the 14th is dropped.
  const after = await refresh(source(handlers, log), evening, 'RGM', () => at('00:20', '2026-10-16'), noSignal)
  assert.deepEqual(Object.keys(after.snapshot.days).sort(), ['2026-10-15', '2026-10-16', '2026-10-17'])
  assert.ok(log.includes('/api/journeys?serviceDate=2026-10-17'))

  // The train that left on the 15th is still on the board, from the previous date's timetable.
  const rows = deriveRows({ stationId: 's-rgm', journeys: journeysOf(after.snapshot), live: after.live, now: at('00:20', '2026-10-16'), timetableOnly: true })
  assert.deepEqual(rows.map((r) => r.trainNumber), ['SIM-9001'])
  assert.equal(rows[0].scheduledAt, at('00:26', '2026-10-16'))
})

// --- The ETag client -----------------------------------------------------------------------

const realFetch = globalThis.fetch
afterEach(() => {
  globalThis.fetch = realFetch
})

test('a 304 gives back the saved body, sends If-None-Match, and still carries the server clock', async () => {
  const requests: Headers[] = []
  globalThis.fetch = (async (_url: string | URL | Request, init?: RequestInit) => {
    const headers = new Headers(init?.headers)
    requests.push(headers)
    const conditional = headers.get('If-None-Match')
    return conditional
      ? new Response(null, { status: 304, headers: { 'X-SmartRail-Server-Time': '2026-10-15T02:03:00.000Z' } })
      : new Response(JSON.stringify({ hello: 'board' }), { status: 200, headers: { ETag: '"abc"', 'X-SmartRail-Server-Time': '2026-10-15T02:00:00.000Z' } })
  }) as typeof fetch

  const client = new FeedClient('https://api.test/')
  const first = await client.get<{ hello: string }>('/api/x', noSignal)
  assert.deepEqual(first.data, { hello: 'board' })
  assert.equal(first.notModified, false)
  assert.equal(requests[0].get('If-None-Match'), null)

  const second = await client.get<{ hello: string }>('/api/x', noSignal)
  assert.deepEqual(second.data, { hello: 'board' })
  assert.equal(second.notModified, true)
  assert.equal(requests[1].get('If-None-Match'), '"abc"')
  assert.equal(second.serverTimeMs, Date.parse('2026-10-15T02:03:00.000Z'))
})

test('an error status is a failure, not data', async () => {
  globalThis.fetch = (async () => new Response('nope', { status: 500 })) as typeof fetch
  await assert.rejects(new FeedClient('https://api.test').get('/api/x', noSignal), ApiError)
})
