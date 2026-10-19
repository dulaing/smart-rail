import assert from 'node:assert/strict'
import { test } from 'node:test'
import { staffFetcher } from '../workspace/changesApi.ts'
import { fakeFetch, json, problem, type Answer, type Call } from '../workspace/testkit.ts'
import { incidentStillOpen, openFromReport, openStandalone, resolve } from './incidentActions.ts'
import type { Incident } from './types.ts'

const open: Incident = {
  incidentId: 'i1',
  sourceReportId: null,
  reasonCode: 'TRAIN_FAULT',
  state: 'OPEN',
  openedById: 'c1',
  openedByName: 'Nimali',
  openedAt: '2026-10-19T08:00:00Z',
  resolvedAt: null,
  changeIds: [],
}
const resolved: Incident = { ...open, state: 'RESOLVED', resolvedAt: '2026-10-19T09:00:00Z' }

function setup(route: (call: Call) => Answer | Promise<Answer>) {
  const { fetcher, calls } = fakeFetch(route)
  return { call: staffFetcher('', async () => 'token', fetcher), calls }
}

test('opening from a report sends the report ID alone and returns the incident', async () => {
  const { call, calls } = setup(() => json({ ...open, sourceReportId: 'r1' }, 201))
  const outcome = await openFromReport(call, 'r1')
  assert.equal(outcome.kind, 'opened')
  assert.deepEqual(calls[0].body, { sourceReportId: 'r1' })
})

test('a standalone incident carries only the reason code and is sent exactly once even when the answer is lost', async () => {
  const { call, calls } = setup(() => 'lost')
  assert.deepEqual(await openStandalone(call, 'OBSTRUCTION'), { kind: 'uncertain', why: 'network' })
  assert.equal(calls.length, 1)
  assert.deepEqual(calls[0].body, { reasonCode: 'OBSTRUCTION' })
})

test('a server error on a standalone incident is uncertain, not a refusal, and does not retry', async () => {
  const { call, calls } = setup(() => problem(503, 'down'))
  assert.deepEqual(await openStandalone(call, 'OBSTRUCTION'), { kind: 'uncertain', why: 'server-error' })
  assert.equal(calls.length, 1)
})

test('refusals and access states are told apart from lost answers', async () => {
  assert.equal((await openStandalone(setup(() => problem(400, 'bad')).call, 'X')).kind, 'refused')
  assert.deepEqual(await openFromReport(setup(() => problem(404, 'none')).call, 'r1'), { kind: 'missing' })
  assert.deepEqual(await openStandalone(setup(() => new Response(null, { status: 403 })).call, 'X'), { kind: 'denied', code: 403 })
})

test('resolving returns the server incident and touches nothing else', async () => {
  const { call, calls } = setup(() => json(resolved))
  const outcome = await resolve(call, 'i1')
  assert.deepEqual(outcome, { kind: 'resolved', incident: resolved })
  assert.deepEqual(calls.map((c) => `${c.method} ${c.path}`), ['POST /api/operations/incidents/i1/resolve'])
})

test('a lost resolve answer is settled by one read, not by resolving again', async () => {
  const { call, calls } = setup((c) => (c.method === 'POST' ? 'lost' : json(resolved)))
  const outcome = await resolve(call, 'i1')
  assert.equal(outcome.kind, 'uncertain')
  assert.deepEqual(calls.map((c) => c.method), ['POST', 'GET'])
  assert.equal(outcome.kind === 'uncertain' && outcome.check.status === 'read' && outcome.check.incident.state, 'RESOLVED')
})

test('whether a draft\'s incident is still open: yes, no, or unknown when it cannot be read', async () => {
  assert.equal(await incidentStillOpen(setup(() => json(open)).call, 'i1'), true)
  assert.equal(await incidentStillOpen(setup(() => json(resolved)).call, 'i1'), false)
  assert.equal(await incidentStillOpen(setup(() => problem(404, 'none')).call, 'i1'), false)
  assert.equal(await incidentStillOpen(setup(() => 'lost').call, 'i1'), null)
  assert.equal(await incidentStillOpen(setup(() => problem(503, 'down')).call, 'i1'), null)
})
