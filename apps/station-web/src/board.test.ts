// Run with: npm test   (node --test src/*.test.ts)
//
// A reminder of how the server places a train: an observation is "progress along the segment from
// one stop to the next". Once any progress is reported on a segment the From stop has been left,
// so the first stop's departure counts as gone as soon as the train has been observed at all.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { callingStops, deriveRows, evidenceOf, graceMs, platformView, type BoardInput, type JourneyLive } from './board.ts'
import { at, changes, journey, running, stations } from './fixtures.ts'
import { stationTexts } from './messages.ts'
import { describeRow } from './rowText.ts'

const j = journey()
const name = (id: string) => stationTexts(stations.find((s) => s.id === id))
const observedAt = (hhmmss: string) => new Date(Date.parse(`2026-10-15T${hhmmss}+05:30`)).toISOString()

/** Rows for one station with the given live data for SIM-1001. */
function rowsFor(station: string, now: number, live: Partial<JourneyLive> | null, overrides: Partial<BoardInput> = {}) {
  const map = new Map<string, JourneyLive>()
  if (live) map.set(j.journeyId, { running: null, changes: null, refreshFailed: false, checkedAt: now, ...live })
  return deriveRows({ stationId: station, journeys: [j], live: map, now, timetableOnly: false, ...overrides })
}

test('a starting train has a departure and no arrival, and a terminating train an arrival and no departure', () => {
  const start = rowsFor('s-gph', at('07:00'), null, { timetableOnly: true })
  assert.equal(start.length, 1)
  assert.equal(start[0].kind, 'departure')
  assert.equal(start[0].startsHere, true)

  const end = rowsFor('s-fot', at('08:00'), null, { timetableOnly: true })
  assert.equal(end.length, 1)
  assert.equal(end[0].kind, 'arrival')
  assert.equal(end[0].endsHere, true)
  assert.equal(end[0].scheduledAt, at('08:30'))
})

test('a delayed train stays after its scheduled time until evidence shows it has left', () => {
  // Ragama's departure was due at 07:51; at 08:00 the train is still on its way there, 15 minutes late.
  const delayed = running({
    observation: { from: 'st-1', to: 'st-2', permille: 500, observedAt: observedAt('07:59:00') },
    estimates: { 'st-2': ['08:05', '08:06'], 'st-3': ['08:35', '08:37'] },
  })
  const rows = rowsFor('s-rgm', at('08:00'), { running: delayed })
  assert.equal(rows.length, 1)
  assert.equal(rows[0].scheduledAt, at('07:51'))
  assert.equal(rows[0].expectedAt, at('08:06'))
  assert.equal(rows[0].delayed, true)
  assert.equal(rows[0].delayMinutes, 15)

  // Once the newest observation puts the train between Ragama and Maradana, Ragama's row goes.
  const departed = running({
    observation: { from: 'st-2', to: 'st-3', permille: 300, observedAt: observedAt('08:07:00') },
    estimates: { 'st-3': ['08:35', '08:37'] },
  })
  assert.equal(rowsFor('s-rgm', at('08:08'), { running: departed }).length, 0)
  assert.equal(rowsFor('s-mda', at('08:08'), { running: departed }).length, 1)
})

test('rows are ordered by when a train is expected, not only by its scheduled time', () => {
  const late = running({
    observation: { from: 'st-1', to: 'st-2', permille: 900, observedAt: observedAt('08:09:00') },
    estimates: { 'st-2': ['08:15', '08:20'] },
  })
  const [row] = rowsFor('s-rgm', at('08:10'), { running: late })
  assert.equal(row.trainNumber, 'SIM-1001')
  assert.equal(row.sortAt, at('08:20'))
})

test('unknown evidence says unknown, hides the expected time and is never on time', () => {
  const old = running({
    freshness: 'UNKNOWN',
    observation: { from: 'st-1', to: 'st-2', permille: 400, observedAt: observedAt('07:44:00') },
  })
  const [row] = rowsFor('s-rgm', at('07:55'), { running: old })
  assert.equal(row.evidence, 'UNKNOWN')
  assert.equal(row.expectedAt, null)
  assert.equal(row.onTime, false)
  const { pills } = describeRow(row, name)
  assert.ok(pills.some((p) => p.id === 'UNKNOWN'))
  assert.ok(!pills.some((p) => p.id === 'on-time'))
})

test('a train nobody ever observed leaves after its grace time; one that was running stays', () => {
  const never = running({ freshness: 'UNKNOWN' })
  assert.equal(rowsFor('s-gph', at('07:35') + graceMs - 1000, { running: never }).length, 1)
  assert.equal(rowsFor('s-gph', at('07:35') + graceMs + 1000, { running: never }).length, 0)

  const wasRunning = running({
    freshness: 'UNKNOWN',
    observation: { from: 'st-1', to: 'st-2', permille: 400, observedAt: observedAt('07:44:00') },
  })
  assert.equal(rowsFor('s-rgm', at('08:30'), { running: wasRunning }).length, 1)
})

test('evidence follows the shared freshness fixture, measured from the observation and not the response', () => {
  const fixture = JSON.parse(
    readFileSync(new URL('../../../tests/SmartRail.Api.Tests/Rail/freshness-vectors.json', import.meta.url), 'utf8'),
  ) as { cases: { name: string; observedAt: string | null; now: string; freshness: string }[] }

  for (const vector of fixture.cases) {
    const live = running({
      // The server's own word is CURRENT: the client ages the observation itself.
      freshness: 'CURRENT',
      observation: vector.observedAt ? { from: 'st-1', to: 'st-2', permille: 0, observedAt: vector.observedAt } : null,
    })
    assert.equal(evidenceOf(live, Date.parse(vector.now)), vector.freshness, vector.name)
  }
})

test('between polls, and after a 304 reuses the same body, evidence ages and the expected time disappears', () => {
  const body = running({
    observation: { from: 'st-1', to: 'st-2', permille: 400, observedAt: observedAt('07:30:00') },
    estimates: { 'st-2': ['07:50', '07:51'] },
  })
  const rowAt = (hhmmss: string) => rowsFor('s-rgm', Date.parse(`2026-10-15T${hhmmss}+05:30`), { running: body })[0]
  assert.equal(rowAt('07:32:59').evidence, 'CURRENT')
  assert.equal(rowAt('07:32:59').expectedAt, at('07:51'))
  assert.equal(rowAt('07:33:00').evidence, 'AGEING')
  assert.equal(rowAt('07:33:59').expectedAt, at('07:51'))
  assert.equal(rowAt('07:34:00').evidence, 'UNKNOWN')
  assert.equal(rowAt('07:34:00').expectedAt, null)
})

test('a cancelled train is a notice, never an operating departure or a next train', () => {
  const live = {
    running: running({ availability: 'CANCELLED' }),
    changes: changes([
      { changeId: 'c1', kind: 'CANCEL', confirmedAt: observedAt('07:00:00'), delayMinutes: null, newPlatform: null, targetStopId: null, finalStopId: null },
    ]),
  }
  const rows = rowsFor('s-gph', at('07:20'), live)
  assert.equal(rows.length, 1)
  assert.equal(rows[0].cancelled, true)
  assert.equal(rows[0].expectedAt, null)
  assert.equal(platformView(rows, '2').next, null)
  assert.equal(platformView(rows, '2').notices.length, 1)
  const { pills, notes } = describeRow(rows[0], name)
  assert.deepEqual(pills.map((p) => p.id), ['cancelled'])
  assert.equal(notes[0].en, 'Staff-confirmed change · 07:00')
})

test('an early ending keeps the earlier served stops and the new final stop, and leaves later stops unserved', () => {
  const ending = { availability: 'ENDS_EARLY' as const, finalStopId: 'st-3', unserved: ['st-4'] }
  const early = running({
    ...ending,
    estimates: { 'st-2': ['07:50', '07:51'], 'st-3': ['08:20', null] },
    observation: { from: 'st-1', to: 'st-2', permille: 100, observedAt: observedAt('07:36:00') },
  })
  const now = at('07:37')

  // Before it has left, Gampaha shows the train going to the new final stop, not on to Fort.
  const [origin] = rowsFor('s-gph', at('07:30'), { running: running(ending) })
  assert.equal(origin.endsEarly, true)
  assert.equal(origin.endsAtStationId, 's-mda')
  assert.equal(origin.otherStationId, 's-mda', 'the destination is the new final stop')

  const [final] = rowsFor('s-mda', now, { running: early })
  assert.equal(final.kind, 'arrival')
  assert.equal(final.endsHere, true)
  assert.equal(final.expectedAt, at('08:20'))

  const [unserved] = rowsFor('s-fot', now, { running: early })
  assert.equal(unserved.unservedHere, true)
  assert.equal(unserved.expectedAt, null)
  assert.equal(platformView([unserved], '4').next, null)
  const text = describeRow(unserved, name)
  assert.ok(text.notes.some((n) => n.en === 'Train ends at Maradana'))
  assert.ok(text.notes.some((n) => n.en === 'Does not stop here'))

  const stops = callingStops(j, early, 'st-1')
  assert.deepEqual(stops.map((s) => [s.stopId, s.served]), [['st-2', true], ['st-3', true], ['st-4', false]])
})

test('a platform move is visible at the original platform and at the new one', () => {
  const moved = running({
    platforms: { 'st-3': ['5', observedAt('08:00:00')] },
    estimates: { 'st-3': ['08:20', '08:22'] },
    observation: { from: 'st-2', to: 'st-3', permille: 400, observedAt: observedAt('08:10:00') },
  })
  const now = at('08:11')
  const rows = rowsFor('s-mda', now, { running: moved })
  assert.equal(rows[0].platform, '5')
  assert.deepEqual(rows[0].moved, { from: '3', to: '5', confirmedAt: at('08:00') })

  // Platform 3 (the original): no next train, but a notice.
  const original = platformView(rows, '3')
  assert.equal(original.next, null)
  assert.equal(original.notices.length, 1)
  assert.equal(describeRow(original.notices[0], name).notes[0].en, 'Moved from platform 3 to platform 5 · Staff-confirmed change · 08:00')
  // Platform 5 (the new one): the train is the next train, carrying the same move.
  const target = platformView(rows, '5')
  assert.equal(target.next?.journeyId, 'j-1001')
  assert.equal(describeRow(target.next!, name).pills.some((p) => p.id === 'platform-change'), true)
  // One row per journey and stop, however often the same read repeats.
  assert.equal(rowsFor('s-mda', now, { running: moved }).length, 1)
})

test('a confirmed platform stays visible when position becomes unknown, with its own time', () => {
  const unknown = running({
    freshness: 'UNKNOWN',
    platforms: { 'st-3': ['5', observedAt('08:00:00')] },
    observation: { from: 'st-2', to: 'st-3', permille: 400, observedAt: observedAt('07:40:00') },
  })
  const [row] = rowsFor('s-mda', at('08:12'), { running: unknown })
  assert.equal(row.platform, '5')
  assert.equal(row.moved?.confirmedAt, at('08:00'))
  assert.equal(row.expectedAt, null)
})

test('the move notice goes once the train has provably left, or the move is withdrawn', () => {
  const left = running({
    platforms: { 'st-3': ['5', observedAt('08:00:00')] },
    observation: { from: 'st-3', to: 'st-4', permille: 500, observedAt: observedAt('08:25:00') },
  })
  assert.equal(rowsFor('s-mda', at('08:26'), { running: left }).length, 0)

  const withdrawn = running({
    observation: { from: 'st-2', to: 'st-3', permille: 400, observedAt: observedAt('08:10:00') },
    estimates: { 'st-3': ['08:20', '08:22'] },
  })
  const rows = rowsFor('s-mda', at('08:11'), { running: withdrawn })
  assert.equal(rows[0].moved, null)
  assert.equal(platformView(rows, '3').next?.platform, '3')
})

test('with the whole feed lost, rows are timetable only: no expected time, override, status or notice', () => {
  const live = running({
    platforms: { 'st-3': ['5', observedAt('08:00:00')] },
    estimates: { 'st-3': ['08:40', '08:42'] },
    observation: { from: 'st-2', to: 'st-3', permille: 400, observedAt: observedAt('08:10:00') },
  })
  const rows = rowsFor('s-mda', at('08:11'), { running: live }, { timetableOnly: true })
  assert.equal(rows.length, 1)
  assert.equal(rows[0].expectedAt, null)
  assert.equal(rows[0].platform, '3')
  assert.equal(rows[0].moved, null)
  assert.deepEqual(describeRow(rows[0], name), { pills: [], notes: [] })
})

test('a staff-confirmed delay is attributed without a name or cause, and survives unknown position', () => {
  const live = {
    running: running({ freshness: 'UNKNOWN', confirmedDelayMinutes: 15 }),
    changes: changes([
      { changeId: 'c2', kind: 'DELAY', confirmedAt: observedAt('07:10:00'), delayMinutes: 15, newPlatform: null, targetStopId: null, finalStopId: null },
    ]),
  }
  const [row] = rowsFor('s-gph', at('07:30'), live)
  assert.equal(row.delayed, true)
  assert.equal(row.delayMinutes, 15)
  assert.equal(row.expectedAt, null)
  const { pills, notes } = describeRow(row, name)
  assert.ok(pills.some((p) => p.id === 'delayed'))
  assert.ok(notes.some((n) => n.en === 'Staff-confirmed delay · 07:10'))
  for (const note of notes) assert.doesNotMatch(note.en, /controller|driver|because|reason/i)
})

test('one train that failed to refresh is marked on its own row; other rows are untouched', () => {
  const other = journey('SIM-1003', 'j-1003')
  const live = new Map<string, JourneyLive>([
    ['j-1001', { running: running(), changes: null, refreshFailed: true, checkedAt: at('07:25') }],
    ['j-1003', { running: running({ id: 'j-1003' }), changes: null, refreshFailed: false, checkedAt: at('07:30') }],
  ])
  const rows = deriveRows({ stationId: 's-gph', journeys: [j, other], live, now: at('07:31'), timetableOnly: false })
  const failed = rows.find((r) => r.trainNumber === 'SIM-1001')!
  assert.equal(failed.refreshFailed, true)
  assert.equal(rows.find((r) => r.trainNumber === 'SIM-1003')?.refreshFailed, false)
  assert.equal(describeRow(failed, name).notes.at(-1)?.en, 'Could not update · last checked 07:25')
})

test('a station that appears twice in a pattern gets a row for each stop occurrence', () => {
  const loop = journey('SIM-LOOP', 'j-loop', undefined, [
    ['l-1', 's-gph', null, '09:00', '1'],
    ['l-2', 's-rgm', '09:10', '09:11', '1'],
    ['l-3', 's-gph', '09:30', '09:32', '2'],
    ['l-4', 's-fot', '10:00', null, '4'],
  ])
  const rows = deriveRows({ stationId: 's-gph', journeys: [loop], live: new Map(), now: at('08:55'), timetableOnly: true })
  assert.deepEqual(rows.map((r) => r.key), ['j-loop:l-1', 'j-loop:l-3'])
})

test('a staff-confirmed delay keeps an unobserved train on the board past its scheduled time', () => {
  const delayed = running({ freshness: 'UNKNOWN', confirmedDelayMinutes: 30 })
  // 07:35 plus 30 minutes plus the 15 minute grace is 08:20.
  assert.equal(rowsFor('s-gph', at('08:19'), { running: delayed }).length, 1)
  assert.equal(rowsFor('s-gph', at('08:21'), { running: delayed }).length, 0)
})
