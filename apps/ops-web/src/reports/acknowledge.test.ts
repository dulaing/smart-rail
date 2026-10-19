import assert from 'node:assert/strict'
import { test } from 'node:test'
import { staffFetcher } from '../workspace/changesApi.ts'
import { fakeFetch, json, problem, type Answer, type Call } from '../workspace/testkit.ts'
import { acknowledge, recheck, settledReport } from './acknowledge.ts'
import { listReports, defaultFilter, readReport, reportsQuery } from './reportsApi.ts'
import type { ControlReport } from './types.ts'

const waiting: ControlReport = {
  reportId: 'r1',
  journeyId: 'j1',
  reasonCode: 'SIGNAL_DELAY',
  submittedById: 'd1',
  submittedByName: 'Kasun',
  receivedAt: '2026-10-19T08:00:00Z',
  state: 'SENT_AWAITING_CONTROL',
  acknowledgedById: null,
  acknowledgedByName: null,
  acknowledgedAt: null,
}
const acknowledgedBy = (id: string, name: string): ControlReport => ({
  ...waiting,
  state: 'ACKNOWLEDGED',
  acknowledgedById: id,
  acknowledgedByName: name,
  acknowledgedAt: '2026-10-19T08:05:00Z',
})

function setup(route: (call: Call) => Answer | Promise<Answer>) {
  const { fetcher, calls } = fakeFetch(route)
  const call = staffFetcher('', async () => 'token', fetcher)
  return { call, calls }
}

const posts = (calls: Call[]) => calls.filter((c) => c.method === 'POST')

test('acknowledging sends one request with the token and returns the server\'s name and time', async () => {
  const { call, calls } = setup(() => json(acknowledgedBy('me', 'Nimali')))
  const outcome = await acknowledge(call, 'r1', 'me')
  assert.equal(outcome.kind, 'acknowledged')
  assert.equal(outcome.kind === 'acknowledged' && outcome.byMe, true)
  assert.equal(posts(calls).length, 1)
  assert.equal(calls[0].path, '/api/operations/reports/r1/acknowledge')
  assert.equal(calls[0].headers.authorization, 'Bearer token')
})

test('when another controller won, the answer carries their original name and time, not ours', async () => {
  const { call } = setup(() => json(acknowledgedBy('other', 'Ruwan')))
  const outcome = await acknowledge(call, 'r1', 'me')
  assert.equal(outcome.kind, 'acknowledged')
  if (outcome.kind !== 'acknowledged') return
  assert.equal(outcome.byMe, false)
  assert.equal(outcome.report.acknowledgedByName, 'Ruwan')
  assert.equal(outcome.report.acknowledgedAt, '2026-10-19T08:05:00Z')
})

test('a lost answer is settled by one read and is never acknowledged a second time', async () => {
  const { call, calls } = setup((c) => (c.method === 'POST' ? 'lost' : json(acknowledgedBy('me', 'Nimali'))))
  const outcome = await acknowledge(call, 'r1', 'me')
  assert.equal(outcome.kind, 'uncertain')
  if (outcome.kind !== 'uncertain') return
  assert.equal(outcome.why, 'network')
  assert.equal(outcome.check.status, 'read')
  assert.equal(posts(calls).length, 1)
  assert.deepEqual(calls.map((c) => c.method), ['POST', 'GET'])
})

test('a lost answer followed by an unreadable report stays uncertain and invents nothing', async () => {
  const { call, calls } = setup(() => 'lost')
  const outcome = await acknowledge(call, 'r1', 'me')
  assert.deepEqual(outcome, { kind: 'uncertain', why: 'network', check: { status: 'failed' } })
  assert.equal(posts(calls).length, 1)
})

test('a server error is uncertain, a 404 is a vanished report and a refusal says nothing was recorded', async () => {
  assert.equal((await acknowledge(setup(() => problem(503, 'down')).call, 'r1', 'me')).kind, 'uncertain')
  assert.deepEqual(await acknowledge(setup(() => problem(404, 'No report has this ID.')).call, 'r1', 'me'), { kind: 'missing' })
  const refused = await acknowledge(setup(() => problem(400, 'bad')).call, 'r1', 'me')
  assert.equal(refused.kind, 'refused')
})

test('401 and 403 are access states, not connection problems', async () => {
  assert.deepEqual(await acknowledge(setup(() => new Response(null, { status: 403 })).call, 'r1', 'me'), { kind: 'denied', code: 403 })
  assert.deepEqual(await acknowledge(setup(() => new Response(null, { status: 401 })).call, 'r1', 'me'), { kind: 'denied', code: 401 })
})

test('rechecking only ever reads', async () => {
  const { call, calls } = setup(() => json(waiting))
  assert.deepEqual(await recheck(call, 'r1'), { status: 'read', report: waiting })
  assert.deepEqual(calls.map((c) => c.method), ['GET'])
})

test('opening a report and listing the inbox are reads and never reach the acknowledge route', async () => {
  const { call, calls } = setup((c) => (c.path.startsWith('/api/operations/reports?') ? json({ items: [waiting], nextCursor: null }) : json(waiting)))
  await listReports(call, defaultFilter, null)
  await readReport(call, 'r1')
  assert.deepEqual(calls.map((c) => c.method), ['GET', 'GET'])
  assert.ok(calls.every((c) => !c.path.endsWith('/acknowledge')))
})

test('the inbox query carries the filter and returns the cursor exactly as given', () => {
  assert.equal(reportsQuery(defaultFilter, null), '?acknowledged=false&limit=25')
  assert.equal(reportsQuery({ acknowledged: 'yes', journeyId: null }, null), '?acknowledged=true&limit=25')
  assert.equal(reportsQuery({ acknowledged: 'all', journeyId: 'j 1' }, 'a+b/c='), '?journeyId=j%201&limit=25&cursor=a%2Bb%2Fc%3D')
})

test('the settled report prefers any copy that carries the recorded acknowledgement', () => {
  const won = acknowledgedBy('other', 'Ruwan')
  assert.equal(settledReport(waiting, { kind: 'acknowledged', report: won, byMe: false }), won)
  assert.equal(settledReport(won, null), won)
  assert.equal(settledReport(waiting, { kind: 'uncertain', why: 'timeout', check: { status: 'read', report: won } }), won)
  assert.equal(settledReport(waiting, { kind: 'uncertain', why: 'timeout', check: { status: 'failed' } }), waiting)
  assert.equal(settledReport(null, { kind: 'uncertain', why: 'timeout', check: { status: 'read', report: waiting } }), waiting)
})
