import type { Fetcher } from '../workspace/api.ts'
import type { Page } from './paged.ts'
import { readAudit, type AuditSubject } from './reportsApi.ts'
import type { AuditEntry } from './types.ts'

// One page of an audit trail, sorted into what a screen must tell apart. The trail is read a
// bounded page at a time; `page.nextCursor` is null exactly when the history has ended, and the
// screen may say "complete" only then.

export type AuditLoad =
  | { kind: 'ok'; page: Page<AuditEntry> }
  | { kind: 'denied'; code: 401 | 403 }
  | { kind: 'missing' }
  /** The server refused the request itself (for example a cursor it does not know). */
  | { kind: 'refused'; detail: string }
  /** No usable answer: the connection or the server failed. This is not a permission problem. */
  | { kind: 'failed' }

export async function loadAuditPage(call: Fetcher, subject: AuditSubject, cursor: string | null): Promise<AuditLoad> {
  const result = await readAudit(call, subject, cursor)
  switch (result.kind) {
    case 'ok':
      return { kind: 'ok', page: { items: result.body.entries, nextCursor: result.body.nextCursor } }
    case 'denied':
      return { kind: 'denied', code: result.status }
    case 'rejected':
      return result.status === 404 ? { kind: 'missing' } : { kind: 'refused', detail: `${result.title}: ${result.detail}` }
    default:
      return { kind: 'failed' }
  }
}
