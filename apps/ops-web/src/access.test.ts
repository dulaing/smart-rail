import assert from 'node:assert/strict'
import { test } from 'node:test'
import { canOpenPath, focusScope, homeFor, visibleDestinations } from './access.ts'
const meWith = (...codes: string[]) => ({ permissions: codes.map((code) => ({ code })) })

const controller = meWith('changes.publish', 'incidents.manage', 'incidents.read')
const auditor = meWith('incidents.read', 'audit.read')
const sysAdmin = meWith('accounts.manage', 'grants.manage')

test('only incidents.manage opens the raw report inbox and its report pages', () => {
  assert.equal(canOpenPath(controller, '/reports'), true)
  assert.equal(canOpenPath(controller, '/reports/6f1c0f4e-0000-0000-0000-000000000001'), true)
  assert.equal(canOpenPath(auditor, '/reports'), false)
  assert.equal(canOpenPath(auditor, '/reports/6f1c0f4e-0000-0000-0000-000000000001'), false)
  assert.equal(canOpenPath(sysAdmin, '/reports'), false)
  assert.ok(!visibleDestinations(auditor).some((d) => d.path === '/reports'))
})

test('an unknown path under a workspace is still not a page', () => {
  assert.equal(canOpenPath(controller, '/reports-archive'), false)
  assert.equal(canOpenPath(controller, '/nothing/here'), false)
})

test('a signed-in account with no staff permission lands on its account page', () => {
  assert.equal(homeFor(sysAdmin), '/account')
})

test('moving between reports keeps focus where it is, while moving between workspaces does not', () => {
  assert.equal(focusScope('/reports'), focusScope('/reports/abc'))
  assert.notEqual(focusScope('/reports/abc'), focusScope('/corridor'))
  assert.notEqual(focusScope('/corridor'), focusScope('/corridor/journeys/abc'))
})

test('incidents open for anyone who reads them, and the audit history stays with audit.read', () => {
  assert.equal(canOpenPath(controller, '/incidents'), true)
  assert.equal(canOpenPath(auditor, '/incidents/6f1c0f4e-0000-0000-0000-000000000001'), true)
  assert.equal(canOpenPath(sysAdmin, '/incidents'), false)
})

test('audit history needs audit.read: a controller, a station staff account and a SysAdmin are refused', () => {
  const id = '6f1c0f4e-0000-0000-0000-000000000001'
  assert.equal(canOpenPath(auditor, '/audit'), true)
  assert.equal(canOpenPath(auditor, `/audit/journeys/${id}`), true)
  assert.equal(canOpenPath(auditor, `/audit/incidents/${id}`), true)
  assert.equal(canOpenPath(controller, '/audit'), false)
  assert.equal(canOpenPath(controller, `/audit/incidents/${id}`), false)
  assert.equal(canOpenPath(meWith('changes.publish_platform'), '/audit'), false)
  assert.equal(canOpenPath(sysAdmin, `/audit/journeys/${id}`), false)
})

test('an auditor is offered incident and audit pages and never the raw report inbox', () => {
  const paths = visibleDestinations(auditor).map((d) => d.path)
  assert.ok(paths.includes('/incidents') && paths.includes('/audit'))
  assert.ok(!paths.includes('/reports'))
})
