// Run with: npm test   (node --test src/*.test.ts)
import assert from 'node:assert/strict'
import { test } from 'node:test'
import { parseConfig, platformsAt, validateConfig } from './config.ts'
import { at, journey, stations } from './fixtures.ts'
import { emptySnapshot, loadSnapshot, pruneSnapshot, saveSnapshot, snapshotKey, snapshotVersion } from './snapshot.ts'
import { addDays, colomboDate, formatClock, serviceDatesAround } from './time.ts'

test('a board is configured by station, view and platform, with no silent default station', () => {
  assert.deepEqual(parseConfig('?station=FOT&view=entrance'), { ok: true, config: { station: 'FOT', view: 'entrance', platform: null } })
  assert.deepEqual(parseConfig('?station=GPH&view=platform&platform=2'), { ok: true, config: { station: 'GPH', view: 'platform', platform: '2' } })
  // A waiting area uses the entrance view, and the view defaults to it.
  assert.deepEqual(parseConfig('?station=GPH'), { ok: true, config: { station: 'GPH', view: 'entrance', platform: null } })
  assert.deepEqual(parseConfig('', { station: 'FOT', view: 'platform', platform: '4' }), { ok: true, config: { station: 'FOT', view: 'platform', platform: '4' } })

  assert.deepEqual(parseConfig(''), { ok: false, problem: 'missing-station' })
  assert.deepEqual(parseConfig('?station=GPH&view=carousel'), { ok: false, problem: 'bad-view' })
  assert.deepEqual(parseConfig('?station=GPH&view=platform'), { ok: false, problem: 'missing-platform' })
})

test('an unknown station or platform is a configuration error, never another station', () => {
  const journeys = [journey()]
  const entrance = parseConfig('?station=gph')
  assert.ok(entrance.ok)
  const known = validateConfig(entrance.config, stations, journeys)
  assert.ok(known.ok && known.station.id === 's-gph', 'a station code matches in any case')

  const byId = parseConfig('?station=s-fot')
  assert.ok(byId.ok && validateConfig(byId.config, stations, journeys).ok, 'a station ID matches too')

  const stranger = parseConfig('?station=XXX')
  assert.ok(stranger.ok)
  assert.deepEqual(validateConfig(stranger.config, stations, journeys), { ok: false, problem: 'unknown-station' })

  const badPlatform = parseConfig('?station=GPH&view=platform&platform=9')
  assert.ok(badPlatform.ok)
  assert.deepEqual(validateConfig(badPlatform.config, stations, journeys), { ok: false, problem: 'unknown-platform' })
  assert.deepEqual([...platformsAt(journeys, 's-gph')], ['2'])

  // With no timetable there is nothing to judge a platform by yet, so it is not called wrong.
  assert.ok(validateConfig(badPlatform.config, stations, []).ok)
})

test('the saved snapshot round trips, and an unknown version or damaged text is thrown away', () => {
  const data = new Map<string, string>()
  const store = { getItem: (k: string) => data.get(k) ?? null, setItem: (k: string, v: string) => void data.set(k, v) }
  assert.equal(loadSnapshot(store), null, 'first boot has nothing saved')

  const snapshot = { ...emptySnapshot(), stations, stationsSavedAt: 5, days: { '2026-10-15': { serviceDate: '2026-10-15', downloadedAt: at('07:00'), journeys: [journey()] } } }
  saveSnapshot(store, snapshot)
  assert.deepEqual(loadSnapshot(store), snapshot)

  data.set(snapshotKey, JSON.stringify({ ...snapshot, version: snapshotVersion + 1 }))
  assert.equal(loadSnapshot(store), null)
  data.set(snapshotKey, '{not json')
  assert.equal(loadSnapshot(store), null)
  data.set(snapshotKey, JSON.stringify({ version: snapshotVersion, stations: 'no', days: null }))
  assert.equal(loadSnapshot(store), null)
})

test('the saved snapshot holds the public timetable only: nothing about running, platforms in force or changes', () => {
  const text = JSON.stringify({ ...emptySnapshot(), days: { d: { serviceDate: 'd', downloadedAt: 1, journeys: [journey()] } } })
  for (const word of ['estimated', 'confirmed', 'observ', 'token', 'freshness']) {
    assert.ok(!text.toLowerCase().includes(word), `snapshot mentions "${word}"`)
  }
})

test('a full storage does not break the board', () => {
  const store = {
    getItem: () => null,
    setItem: () => {
      throw new Error('QuotaExceededError')
    },
  }
  assert.doesNotThrow(() => saveSnapshot(store, emptySnapshot()))
})

test('pruning keeps yesterday and later only', () => {
  const day = (date: string) => ({ serviceDate: date, downloadedAt: 0, journeys: [] })
  const pruned = pruneSnapshot({ ...emptySnapshot(), days: { '2026-10-13': day('2026-10-13'), '2026-10-14': day('2026-10-14'), '2026-10-16': day('2026-10-16') } }, '2026-10-14')
  assert.deepEqual(Object.keys(pruned.days), ['2026-10-14', '2026-10-16'])
})

test('service dates and the clock are Colombo time, whatever the device zone is', () => {
  // 18:30 UTC on the 15th is 00:00 on the 16th in Colombo (UTC+5:30).
  const midnight = Date.parse('2026-10-15T18:30:00Z')
  assert.equal(colomboDate(midnight - 1000), '2026-10-15')
  assert.equal(colomboDate(midnight), '2026-10-16')
  assert.equal(formatClock(midnight), '00:00')
  assert.deepEqual(serviceDatesAround(midnight), ['2026-10-15', '2026-10-16', '2026-10-17'])
  assert.equal(addDays('2026-10-31', 1), '2026-11-01')
})
