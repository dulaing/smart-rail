import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createReader } from './api.ts'
import { confirmReview } from './changeActions.ts'
import { staffFetcher } from './changesApi.ts'
import { buildRequest, confirmationOf, emptyDraft, flowReducer, initialFlow, type FlowState, type Review } from './changeFlow.ts'
import { confirmedState, fakeFetch, json, problem } from './testkit.ts'
import type { ChangePreview } from './types.ts'

const preview: ChangePreview = {
  action: 'PUBLISH',
  change: { kind: 'DELAY', delayMinutes: 5, newPlatform: null, targetStopId: null, finalStopId: null },
  withdrawsChangeId: null,
  previewedVersion: 7,
  before: {
    availability: 'AS_SCHEDULED', confirmedDelayMinutes: null, finalStopId: null, orderClosed: false,
    blockedPickupStopIds: [], stops: [],
  },
  after: {
    availability: 'AS_SCHEDULED', confirmedDelayMinutes: 5, finalStopId: null, orderClosed: false,
    blockedPickupStopIds: [], stops: [],
  },
  affectedStops: [],
  screens: [],
  food: {
    orderClosedBefore: false, orderClosedAfter: false, newlyBlockedStopIds: [], reopenedStopIds: [],
    etaChanges: true, affectedOrderCount: null,
  },
  earlyEnd: null,
  incidentId: null,
}

const review: Review = { request: { draft: { kind: 'DELAY', delayMinutes: 5 } }, preview }

function reviewingState(): FlowState {
  let state = initialFlow()
  state = flowReducer(state, { type: 'previewStarted' })
  return flowReducer(state, { type: 'previewed', requestId: 1, request: review.request, preview })
}

function dispatchTo(state: FlowState) {
  let current = state
  const actions: Parameters<typeof flowReducer>[1][] = []
  return {
    actions,
    get state() { return current },
    dispatch(action: Parameters<typeof flowReducer>[1]) {
      actions.push(action)
      current = flowReducer(current, action)
    },
  }
}

test('builds the four supported change requests using only their fields', () => {
  assert.deepEqual(buildRequest({ type: 'publish', draft: { ...emptyDraft('DELAY'), delayMinutes: '12' } }), {
    ok: true, request: { draft: { kind: 'DELAY', delayMinutes: 12 } },
  })
  assert.deepEqual(buildRequest({ type: 'publish', draft: emptyDraft('CANCEL') }), {
    ok: true, request: { draft: { kind: 'CANCEL' } },
  })
  assert.deepEqual(buildRequest({ type: 'publish', draft: { ...emptyDraft('PLATFORM'), targetStopId: 'stop-1', newPlatform: '  4A  ' } }), {
    ok: true, request: { draft: { kind: 'PLATFORM', targetStopId: 'stop-1', newPlatform: '4A' } },
  })
  assert.deepEqual(buildRequest({ type: 'publish', draft: { ...emptyDraft('EARLY_END'), finalStopId: 'stop-2', incidentId: 'incident-1' } }), {
    ok: true, request: { draft: { kind: 'EARLY_END', finalStopId: 'stop-2' }, incidentId: 'incident-1' },
  })
  assert.deepEqual(buildRequest({ type: 'withdraw', changeId: 'change-1' }), {
    ok: true, request: { withdrawChangeId: 'change-1' },
  })
})

test('editing after preview drops the review; confirmation uses the reviewed request and version', () => {
  const state = reviewingState()
  const edited = flowReducer(state, { type: 'edit', draft: { ...emptyDraft('DELAY'), delayMinutes: '8' } })
  assert.equal(edited.step.name, 'editing')
  assert.equal(edited.target.type, 'publish')
  assert.deepEqual(confirmationOf(review), { draft: { kind: 'DELAY', delayMinutes: 5 }, previewedVersion: 7 })

  const confirming = flowReducer(state, { type: 'confirmStarted' })
  assert.equal(confirming.step.name, 'confirming')
  assert.equal(flowReducer(confirming, { type: 'confirmStarted' }), confirming, 'a second confirmation start is ignored')
})

test('a platform-only account starts and resets to the platform form', () => {
  let state = initialFlow('PLATFORM')
  assert.equal(state.target.type === 'publish' && state.target.draft.kind, 'PLATFORM')
  state = flowReducer(state, { type: 'withdraw', changeId: 'change-1' })
  state = flowReducer(state, { type: 'previewStarted' })
  state = flowReducer(state, { type: 'previewFailed', requestId: 1, notice: { tone: 'error', text: 'Not active.' } })
  assert.equal(state.target.type === 'publish' && state.target.draft.kind, 'PLATFORM')
  state = flowReducer(state, { type: 'reset' })
  assert.equal(state.target.type === 'publish' && state.target.draft.kind, 'PLATFORM')
})

test('a stale preview returns to editing and keeps the draft for a new review', () => {
  const confirming = flowReducer(reviewingState(), { type: 'confirmStarted' })
  const stale = flowReducer(confirming, { type: 'stale', latestVersion: 8 })
  assert.equal(stale.step.name, 'editing')
  assert.equal(stale.target.type, 'publish')
  assert.match(stale.notice?.text ?? '', /preview it again and confirm only if it is still right/)
  assert.equal(flowReducer(stale, { type: 'confirmStarted' }), stale, 'confirmation is unavailable without a new review')
})

test('409 STALE_PREVIEW requires a new preview and performs no automatic confirmation retry', async () => {
  const { fetcher, calls } = fakeFetch(() => problem(409, 'The journey changed.', { code: 'STALE_PREVIEW', latestVersion: 8 }))
  const sent = dispatchTo(reviewingState())
  let refreshes = 0
  await confirmReview({
    call: fetcher,
    reader: createReader(fetcher, ''),
    journeyId: 'journey-1',
    dispatch: sent.dispatch,
    onChanged: () => { refreshes += 1 },
  }, review)

  assert.equal(calls.length, 1)
  assert.equal(calls[0].method, 'POST')
  assert.deepEqual(calls[0].body, { draft: { kind: 'DELAY', delayMinutes: 5 }, previewedVersion: 7 })
  assert.equal(sent.state.step.name, 'editing')
  assert.equal(refreshes, 1)
})

test('400 and 404 refusals keep the input and do not claim success', async () => {
  for (const status of [400, 404]) {
    const { fetcher, calls } = fakeFetch(() => problem(status, 'The request is no longer valid.'))
    const sent = dispatchTo(reviewingState())
    await confirmReview({
      call: fetcher,
      reader: createReader(fetcher, ''),
      journeyId: 'journey-1',
      dispatch: sent.dispatch,
      onChanged: () => assert.fail('a refused write does not refresh as confirmed'),
    }, review)
    assert.equal(calls.length, 1)
    assert.equal(sent.state.step.name, 'editing')
    assert.equal(sent.state.target.type, 'publish')
    assert.match(sent.state.notice?.text ?? '', /Nothing was published/)
  }
})

test('401 and 403 stop the flow and ask the staff member to check access', async () => {
  for (const status of [401, 403] as const) {
    const { fetcher, calls } = fakeFetch(() => new Response(null, { status }))
    const sent = dispatchTo(reviewingState())
    await confirmReview({
      call: fetcher,
      reader: createReader(fetcher, ''),
      journeyId: 'journey-1',
      dispatch: sent.dispatch,
      onChanged: () => assert.fail('a denied write does not refresh as confirmed'),
    }, review)
    assert.equal(calls.length, 1)
    assert.deepEqual(sent.state.step, { name: 'denied', status })
  }
})

test('a lost confirmation reads current state and never sends a second write', async () => {
  const current = confirmedState('journey-1', 7)
  const { fetcher, calls } = fakeFetch((call) => call.method === 'POST' ? 'lost' : json(current))
  const call = staffFetcher('http://api.test', async () => 'staff-token', fetcher)
  const sent = dispatchTo(reviewingState())
  let refreshes = 0

  await confirmReview({
    call,
    reader: createReader(fetcher, ''),
    journeyId: 'journey-1',
    dispatch: sent.dispatch,
    onChanged: () => { refreshes += 1 },
  }, review)

  assert.equal(calls.filter((entry) => entry.method === 'POST').length, 1)
  assert.equal(calls.filter((entry) => entry.method === 'GET').length, 1)
  assert.equal(calls[0].headers.authorization, 'Bearer staff-token')
  assert.equal(sent.state.step.name, 'uncertain')
  if (sent.state.step.name === 'uncertain') {
    assert.equal(sent.state.step.reconciliation.status, 'read')
    if (sent.state.step.reconciliation.status === 'read') assert.equal(sent.state.step.reconciliation.state.journeyVersion, 7)
  }
  assert.equal(refreshes, 1)
})
