import type { Me } from '../auth'
import { ReportInbox } from './ReportInbox.tsx'

const reportPath = /^\/reports\/([^/]+)$/

/**
 * Control's report inbox with the selected report beside it. The shell has already checked that
 * this person may open the destination; the API still checks every read and the acknowledgement.
 * The inbox is keyed by account, so nothing read for one person is shown to the next.
 */
export function ReportsWorkspace({ me, path, onRefreshMe }: { me: Me; path: string; onRefreshMe: () => void }) {
  const match = reportPath.exec(path)
  if (path !== '/reports' && !match) {
    return (
      <p className="notice" role="alert">
        There is no page at this address.
      </p>
    )
  }
  return <ReportInbox key={me.accountId} me={me} selectedId={match ? match[1] : null} onRefreshMe={onRefreshMe} />
}
