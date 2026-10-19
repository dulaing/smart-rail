import assert from 'node:assert/strict'
import { test } from 'node:test'
import { staffFetcher } from '../workspace/changesApi.ts'
import { fakeFetch, json, problem } from '../workspace/testkit.ts'
import { loadAuditPage } from './auditPages.ts'
import { emptyPaged, pagedReducer, visibleItems } from './paged.ts'
import type { AuditEntry } from './types.ts'

const entry = (n: number): AuditEntry => ({
  id: `e${String(n).padStart(3, '0')}`,
  actorId: 'a1',
  actorName: 'Someone',
  action: 'report.acknowledge',
  outcome: 'SUCCEEDED',
  targetType: 'REPORT',
  targetId: 'r1',
  occurredAt: new Date(Date.UTC(2026, 9, 19, 8, 0, n)).toISOString(),
  correlationId: `c${n}`,
})
const all = Array.from({ length: 120 }, (_, i) => entry(i))

// A fake trail that pages like the API: oldest first, cursor = index of the next entry.
function trail() {
  return fakeFetch((call) => {
    const url = new URL(call.path, 'http://x')
    const cursor = url.searchParams.get('cursor')
    const limit = Number(url.searchParams.get('limit'))
    if (cursor !== null && !/^c\d+$/.test(cursor)) return problem(400, 'cursor is not a continuation token from this list.')
    const start = cursor === null ? 0 : Number(cursor.slice(1))
    const entries = all.slice(start, start + limit)
    const next = start + limit < all.length ? `c${start + limit}` : null
    return json({ incidentId: 'i1', entries, nextCursor: next, complete: next === null })
  })
}

test('traversing by cursor reaches every entry once, in order, and only the last page is complete', async () => {
  const { fetcher } = trail()
  const call = staffFetcher('', async () => 'token', fetcher)
  const reduce = pagedReducer<AuditEntry>((e) => e.id)
  let state = emptyPaged<AuditEntry>('incident:i1')
  const first = await loadAuditPage(call, { type: 'incident', id: 'i1' }, null)
  assert.equal(first.kind, 'ok')
  if (first.kind !== 'ok') return
  state = reduce(state, { type: 'firstPage', key: state.key, page: first.page })
  assert.notEqual(state.tail, null, 'one page is not the whole history')
  while (state.tail !== null) {
    const from = state.tail
    state = reduce(state, { type: 'moreStarted', key: state.key, from })
    const next = await loadAuditPage(call, { type: 'incident', id: 'i1' }, from)
    assert.equal(next.kind, 'ok')
    if (next.kind !== 'ok') return
    state = reduce(state, { type: 'morePage', key: state.key, from, page: next.page })
  }
  const ids = visibleItems(state, (e) => e.id).map((e) => e.id)
  assert.deepEqual(ids, all.map((e) => e.id))
})

test('the journey and incident trails use their own routes and send the cursor back untouched', async () => {
  const { fetcher, calls } = trail()
  const call = staffFetcher('', async () => 'token', fetcher)
  await loadAuditPage(call, { type: 'journey', id: 'j1' }, 'c50')
  await loadAuditPage(call, { type: 'incident', id: 'i1' }, null)
  assert.equal(calls[0].path, '/api/operations/journeys/j1/audit?limit=50&cursor=c50')
  assert.equal(calls[1].path, '/api/operations/incidents/i1/audit?limit=50')
  assert.ok(calls.every((c) => c.method === 'GET'))
})

test('a bad cursor, an unknown subject, denial and an outage are all told apart', async () => {
  const call = staffFetcher('', async () => 'token', trail().fetcher)
  assert.equal((await loadAuditPage(call, { type: 'incident', id: 'i1' }, 'garbage')).kind, 'refused')
  const status = (code: number) => staffFetcher('', async () => 'token', fakeFetch(() => new Response(null, { status: code })).fetcher)
  assert.deepEqual(await loadAuditPage(status(403), { type: 'incident', id: 'i1' }, null), { kind: 'denied', code: 403 })
  assert.deepEqual(await loadAuditPage(status(401), { type: 'incident', id: 'i1' }, null), { kind: 'denied', code: 401 })
  assert.deepEqual(await loadAuditPage(staffFetcher('', async () => 'token', fakeFetch(() => problem(404, 'none')).fetcher), { type: 'journey', id: 'j' }, null), { kind: 'missing' })
  assert.deepEqual(await loadAuditPage(staffFetcher('', async () => 'token', fakeFetch(() => 'lost').fetcher), { type: 'journey', id: 'j' }, null), { kind: 'failed' })
})
