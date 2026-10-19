import assert from 'node:assert/strict'
import { test } from 'node:test'
import { emptyInbox, filterKey, inboxReducer, visibleReports, type InboxState } from './inbox.ts'
import type { PagedAction } from './paged.ts'
import { defaultFilter } from './reportsApi.ts'
import type { ControlReport } from './types.ts'

const report = (n: number, extra: Partial<ControlReport> = {}): ControlReport => ({
  reportId: `r${n}`,
  journeyId: 'j1',
  reasonCode: 'TRAIN_FAULT',
  submittedById: 'd1',
  submittedByName: 'Driver',
  receivedAt: `2026-10-19T08:${String(60 - n).padStart(2, '0')}:00Z`,
  state: 'SENT_AWAITING_CONTROL',
  acknowledgedById: null,
  acknowledgedByName: null,
  acknowledgedAt: null,
  ...extra,
})

const key = filterKey(defaultFilter)
const run = (state: InboxState, ...actions: PagedAction<ControlReport>[]) => actions.reduce(inboxReducer, state)
const ids = (state: InboxState) => visibleReports(state).map((r) => r.reportId)

test('a first page fills the list and keeps the cursor for load more', () => {
  const state = run(emptyInbox(defaultFilter), { type: 'firstPage', key, page: { items: [report(1), report(2)], nextCursor: 'c2' } })
  assert.deepEqual(ids(state), ['r1', 'r2'])
  assert.equal(state.loaded, true)
  assert.equal(state.tail, 'c2')
})

test('load more continues from the cursor, adds each report once and ends with a null cursor', () => {
  const state = run(
    emptyInbox(defaultFilter),
    { type: 'firstPage', key, page: { items: [report(1), report(2)], nextCursor: 'c2' } },
    { type: 'moreStarted', key, from: 'c2' },
    { type: 'morePage', key, from: 'c2', page: { items: [report(2), report(3)], nextCursor: null } },
  )
  assert.deepEqual(ids(state), ['r1', 'r2', 'r3'])
  assert.equal(state.tail, null)
  assert.equal(state.extended, true)
})

test('a second load-more click while one is in flight is ignored', () => {
  const started = run(
    emptyInbox(defaultFilter),
    { type: 'firstPage', key, page: { items: [report(1)], nextCursor: 'c1' } },
    { type: 'moreStarted', key, from: 'c1' },
  )
  assert.equal(inboxReducer(started, { type: 'moreStarted', key, from: 'c1' }), started)
})

test('a refresh replaces the first page but never swaps in a cursor from it once older pages are loaded', () => {
  const loaded = run(
    emptyInbox(defaultFilter),
    { type: 'firstPage', key, page: { items: [report(2), report(3)], nextCursor: 'c3' } },
    { type: 'moreStarted', key, from: 'c3' },
    { type: 'morePage', key, from: 'c3', page: { items: [report(4), report(5)], nextCursor: 'c5' } },
  )
  // A newer report arrived, so the fresh first page ends sooner and has its own cursor.
  const refreshed = inboxReducer(loaded, { type: 'firstPage', key, page: { items: [report(1), report(2)], nextCursor: 'c2-new' } })
  assert.deepEqual(ids(refreshed), ['r1', 'r2', 'r4', 'r5'])
  assert.equal(refreshed.tail, 'c5')
})

test('a refresh before any older page was loaded does take the new cursor', () => {
  const first = run(emptyInbox(defaultFilter), { type: 'firstPage', key, page: { items: [report(1)], nextCursor: 'a' } })
  assert.equal(inboxReducer(first, { type: 'firstPage', key, page: { items: [report(1)], nextCursor: 'b' } }).tail, 'b')
})

test('a refresh that finds a report acknowledged elsewhere removes it from the unacknowledged list', () => {
  const state = run(
    emptyInbox(defaultFilter),
    { type: 'firstPage', key, page: { items: [report(1), report(2)], nextCursor: null } },
    { type: 'firstPage', key, page: { items: [report(2)], nextCursor: null } },
  )
  assert.deepEqual(ids(state), ['r2'])
})

test('changing the filter starts a new list with no cursor and drops answers for the old filter', () => {
  const old = run(emptyInbox(defaultFilter), { type: 'firstPage', key, page: { items: [report(1)], nextCursor: 'c1' } })
  const all = { acknowledged: 'all' as const, journeyId: null }
  const changed = inboxReducer(old, { type: 'key', key: filterKey(all) })
  assert.deepEqual(ids(changed), [])
  assert.equal(changed.tail, null)
  assert.equal(changed.loaded, false)
  // The slow answer for the unacknowledged filter arrives late and must not appear.
  assert.equal(inboxReducer(changed, { type: 'firstPage', key, page: { items: [report(9)], nextCursor: null } }), changed)
  assert.equal(inboxReducer(changed, { type: 'morePage', key, from: 'c1', page: { items: [report(8)], nextCursor: null } }), changed)
  // Choosing the same filter again keeps the list.
  assert.equal(inboxReducer(old, { type: 'key', key: filterKey(defaultFilter) }), old)
})

test('a load-more answer for a request the list no longer waits on is dropped', () => {
  const state = run(
    emptyInbox(defaultFilter),
    { type: 'firstPage', key, page: { items: [report(1)], nextCursor: 'c1' } },
    { type: 'moreStarted', key, from: 'c1' },
    { type: 'reload' },
    { type: 'firstPage', key, page: { items: [report(1)], nextCursor: 'c1' } },
  )
  assert.equal(inboxReducer(state, { type: 'morePage', key, from: 'c1', page: { items: [report(7)], nextCursor: null } }), state)
})

test('a failed load more keeps the list and allows another try', () => {
  const failed = run(
    emptyInbox(defaultFilter),
    { type: 'firstPage', key, page: { items: [report(1)], nextCursor: 'c1' } },
    { type: 'moreStarted', key, from: 'c1' },
    { type: 'moreFailed', key, from: 'c1' },
  )
  assert.deepEqual(ids(failed), ['r1'])
  assert.equal(failed.loadMoreFailed, true)
  assert.equal(inboxReducer(failed, { type: 'moreStarted', key, from: 'c1' }).loadingFrom, 'c1')
})

test('a patched report shows its newest state wherever it is listed', () => {
  const acknowledged = report(2, { state: 'ACKNOWLEDGED', acknowledgedById: 'c1', acknowledgedByName: 'Nimali', acknowledgedAt: '2026-10-19T09:00:00Z' })
  const state = run(
    emptyInbox(defaultFilter),
    { type: 'firstPage', key, page: { items: [report(1), report(2)], nextCursor: null } },
    { type: 'patch', item: acknowledged },
  )
  assert.equal(visibleReports(state)[1].acknowledgedByName, 'Nimali')
})
