import assert from 'node:assert/strict'
import { test } from 'node:test'
import { actionText, outcomeText, targetPath } from './auditText.ts'

test('actions get words, with unknown codes left exactly as recorded', () => {
  assert.equal(actionText('report.acknowledge'), 'Report acknowledged')
  assert.equal(actionText('change.publish.delay'), 'Published delay')
  assert.equal(actionText('change.withdraw.early_end'), 'Withdrew early ending')
  assert.equal(actionText('something.new'), 'something.new')
  assert.equal(outcomeText('DENIED'), 'Denied')
  assert.equal(outcomeText('ODD'), 'ODD')
})

test('a target links to its own page only when it names an ID', () => {
  assert.equal(targetPath({ targetType: 'JOURNEY', targetId: 'j1' }), '/corridor/journeys/j1')
  assert.equal(targetPath({ targetType: 'REPORT', targetId: 'r1' }), '/reports/r1')
  assert.equal(targetPath({ targetType: 'INCIDENT', targetId: null }), null)
  assert.equal(targetPath({ targetType: 'ACCOUNT', targetId: 'a1' }), null)
})
