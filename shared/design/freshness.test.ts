// Run with: node --test shared/design/freshness.test.ts
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { clockOffsetMs, describeFreshness, freshnessOf } from './freshness.ts'

type Vector = { name: string; observedAt: string | null; now: string; freshness: string }

const fixture = JSON.parse(
  readFileSync(new URL('../../tests/SmartRail.Api.Tests/Rail/freshness-vectors.json', import.meta.url), 'utf8'),
) as { ageingAfterSeconds: number; unknownAfterSeconds: number; cases: Vector[] }

const thresholds = {
  ageingAfterSeconds: fixture.ageingAfterSeconds,
  unknownAfterSeconds: fixture.unknownAfterSeconds,
}

for (const vector of fixture.cases) {
  test(`fixture: ${vector.name}`, () => {
    assert.equal(freshnessOf(vector.observedAt, vector.now, thresholds), vector.freshness)
  })
}

test('the fixture covers the 179, 180, 239 and 240 second boundaries and missing evidence', () => {
  const names = fixture.cases.map((c) => c.name)
  for (const expected of ['179 seconds old', '180 seconds old', '239 seconds old', '240 seconds old', 'no observation']) {
    assert.ok(names.includes(expected), `fixture lacks "${expected}"`)
  }
})

test('built-in fallback thresholds match the fixture', () => {
  const observedAt = '2026-10-15T08:00:00+05:30'
  assert.equal(freshnessOf(observedAt, '2026-10-15T08:02:59+05:30'), 'CURRENT')
  assert.equal(freshnessOf(observedAt, '2026-10-15T08:03:00+05:30'), 'AGEING')
  assert.equal(freshnessOf(observedAt, '2026-10-15T08:04:00+05:30'), 'UNKNOWN')
})

test('an unreadable or missing timestamp is UNKNOWN, never CURRENT', () => {
  assert.equal(freshnessOf('not a date', '2026-10-15T08:00:00+05:30'), 'UNKNOWN')
  assert.equal(freshnessOf(undefined, '2026-10-15T08:00:00+05:30'), 'UNKNOWN')
})

test('a server-corrected clock keeps ageing between polls', () => {
  // The phone clock runs 90 seconds slow. The server said 08:00:00 when the device read 07:58:30.
  const offset = clockOffsetMs('2026-10-15T08:00:00+05:30', '2026-10-15T07:58:30+05:30')
  assert.equal(offset, 90_000)
  const deviceNow = new Date('2026-10-15T08:01:30+05:30').getTime() // really 08:03:00
  assert.equal(freshnessOf('2026-10-15T08:00:00+05:30', deviceNow + offset), 'AGEING')
})

const utcClock = (t: Date) => `${String(t.getUTCHours()).padStart(2, '0')}:${String(t.getUTCMinutes()).padStart(2, '0')}`
const base = new Date('2026-10-15T02:30:00Z')
const secondsAgo = (s: number) => new Date(base.getTime() - s * 1000)
const describe = (over: Partial<Parameters<typeof describeFreshness>[0]>) =>
  describeFreshness({ observedAt: null, now: base, locale: 'en', formatTime: utcClock, ...over })

test('describeFreshness: current, ageing, unknown and none wording', () => {
  assert.equal(describe({ observedAt: secondsAgo(30) }).text, 'Simulated · updated 02:29')
  assert.equal(describe({ observedAt: secondsAgo(200) }).evidence, 'AGEING')
  assert.equal(describe({ observedAt: secondsAgo(240) }).text, 'Position unknown · last update 02:26')
  assert.equal(describe({}).messageKey, 'freshness.none')
})

test('describeFreshness: a saved copy states the download time and makes no claim about position', () => {
  const d = describe({ observedAt: base, savedAt: secondsAgo(720) })
  assert.equal(d.evidence, null)
  assert.equal(d.text, 'Saved copy · downloaded 02:18')
})

test('describeFreshness: offline adds to the evidence wording', () => {
  assert.equal(describe({ observedAt: secondsAgo(200), offline: true }).text, 'Ageing · last update 02:26 · Offline')
})

test('describeFreshness: Sinhala and Tamil come from the shared wording', () => {
  assert.match(describe({ observedAt: secondsAgo(30), locale: 'si' }).text, /02:29/)
  assert.match(describe({ observedAt: secondsAgo(30), locale: 'ta' }).text, /02:29/)
})
