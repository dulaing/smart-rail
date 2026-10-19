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
