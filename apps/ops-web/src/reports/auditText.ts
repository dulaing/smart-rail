import type { AuditEntry } from './types.ts'

// Words for the audit table. The code is always shown beside the words, so nothing is hidden by
// a friendly label, and an action this screen does not know is shown exactly as recorded.

const known: Record<string, string> = {
  'report.submit': 'Driver report sent',
  'report.acknowledge': 'Report acknowledged',
  'incident.open': 'Incident opened',
  'incident.resolve': 'Incident resolved',
  'account.set_status': 'Account status changed',
  'grant.set': 'Role granted',
  'grant.remove': 'Role removed',
}

const kinds: Record<string, string> = {
  delay: 'delay',
  cancel: 'cancellation',
  platform: 'platform change',
  early_end: 'early ending',
}

/** change.publish.delay -> "Published delay". Unknown codes come back unchanged. */
export function actionText(action: string): string {
  if (known[action]) return known[action]
  const change = /^change\.(publish|withdraw)\.([a-z_]+)$/.exec(action)
  if (change) {
    const kind = kinds[change[2]] ?? change[2]
    return `${change[1] === 'publish' ? 'Published' : 'Withdrew'} ${kind}`
  }
  return action
}

/** A denied attempt is not an error of the page: it is a recorded fact, shown plainly. */
export function outcomeText(outcome: string): string {
  if (outcome === 'SUCCEEDED') return 'Succeeded'
  if (outcome === 'DENIED') return 'Denied'
  return outcome
}

export const targetTypeText = (type: string): string => ({ JOURNEY: 'Journey', REPORT: 'Driver report', INCIDENT: 'Incident' })[type] ?? type

/** Where a target's own page is, when the viewer may open it; null otherwise. */
export function targetPath(entry: Pick<AuditEntry, 'targetType' | 'targetId'>): string | null {
  if (entry.targetId === null) return null
  switch (entry.targetType) {
    case 'JOURNEY':
      return `/corridor/journeys/${entry.targetId}`
    case 'INCIDENT':
      return `/incidents/${entry.targetId}`
    case 'REPORT':
      return `/reports/${entry.targetId}`
    default:
      return null
  }
}
