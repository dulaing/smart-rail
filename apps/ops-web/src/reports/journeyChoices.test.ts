import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createReader } from '../workspace/api.ts'
import { fakeFetch, journey, json } from '../workspace/testkit.ts'
import { journeyLabel, loadJourneyChoices } from './journeyChoices.ts'

test('labels recent journeys by train, date and route, and falls back to a short ID', async () => {
  const now = Date.parse('2026-10-19T09:00:00+05:30')
  const { fetcher } = fakeFetch((call) => {
    const date = new URL(call.path, 'http://x').searchParams.get('serviceDate')
    return json({ serviceDate: date, scheduleNote: '', journeys: date === '2026-10-19' ? [journey('j-today', '1015', '2026-10-19')] : [] })
  })
  const { choices, failed } = await loadJourneyChoices(createReader(fetcher, ''), now)
  assert.equal(failed, false)
  assert.deepEqual(choices.map((c) => c.label), ['Train 1015 · 2026-10-19 · GPH–FOT'])
  assert.equal(journeyLabel(choices, 'j-today'), 'Train 1015 · 2026-10-19 · GPH–FOT')
  assert.equal(journeyLabel(choices, '12345678-aaaa-bbbb-cccc-dddddddddddd'), 'Journey 12345678')
})

test('a failed timetable read is reported without losing the other date', async () => {
  const now = Date.parse('2026-10-19T09:00:00+05:30')
  const { fetcher } = fakeFetch((call) =>
    call.path.includes('2026-10-18') ? 'lost' : json({ serviceDate: '2026-10-19', scheduleNote: '', journeys: [journey('j-today', '1015', '2026-10-19')] }),
  )
  const { choices, failed } = await loadJourneyChoices(createReader(fetcher, ''), now)
  assert.equal(failed, true)
  assert.equal(choices.length, 1)
})
