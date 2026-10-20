import type { Me } from './auth'

export function AccountPage({
  me,
  onRefresh,
}: {
  me: Me
  onRefresh: () => void
}) {
  const roles = me.grants.map((grant) => roleName(grant.role)).join(', ') || 'Staff'
  const workspace = me.permissions.some((permission) => permission.code === 'changes.publish' && permission.scope === 'NETWORK')
    ? 'pilot network'
    : me.permissions.some((permission) => permission.code === 'audit.read')
      ? 'read only'
      : 'staff workspace'
  const permissions = [...me.permissions].sort((a, b) => accessOrder(a.code) - accessOrder(b.code))

  return (
    <section aria-label="Your account" className="account-page">
      <div className="account-toolbar">
        <p className="muted">Your current SmartRail workspace and access</p>
      </div>
      <div className="account-layout">
        <section className="account-card account-profile" aria-label="Staff identity">
          <svg className="account-person" viewBox="0 0 48 48" aria-hidden="true">
            <circle cx="24" cy="14" r="9" />
            <path d="M5 43c2-10 9-15 19-15s17 5 19 15" />
          </svg>
          <h2>{me.displayName}</h2>
          <p>{roles} · {workspace}</p>
          <p className="muted">Account status: {statusName(me.status)}</p>
          {me.grants.some((grant) => grant.stationId) && (
            <p className="muted">Some access is limited to its assigned station.</p>
          )}
        </section>
        <section className="account-card account-access" aria-labelledby="current-access-heading">
          <h2 id="current-access-heading">Current access</h2>
          {permissions.length === 0 ? (
            <p className="muted">No Operations permissions are currently assigned.</p>
          ) : (
            <ul className="access-list">
              {permissions.map((permission) => (
                <li key={permission.code}>{permissionLabel(permission.code)}</li>
              ))}
            </ul>
          )}
        </section>
      </div>
      <div className="account-note">
        <p><strong>Sign-in is managed by the staff identity service.</strong><span>SmartRail checks the active account and permissions on protected requests.</span></p>
        <button type="button" onClick={onRefresh}>Refresh access</button>
      </div>
    </section>
  )
}

function roleName(role: string): string {
  return role.toLowerCase().split('_').map((part) => `${part[0]?.toUpperCase() ?? ''}${part.slice(1)}`).join(' ')
}

function statusName(status: string): string {
  return status.toLowerCase().replaceAll('_', ' ').replace(/\b\w/g, (letter) => letter.toUpperCase())
}

function permissionLabel(code: string): string {
  const labels: Record<string, string> = {
    'changes.publish': 'Publish and withdraw service changes',
    'changes.publish_platform': 'Confirm station-scoped platform changes',
    'reports.acknowledge': 'Read and acknowledge driver reports',
    'incidents.manage': 'Create and resolve incidents',
    'incidents.read': 'Read incidents and incident history',
    'audit.read': 'Read protected audit history',
    'simulation.control': 'Control the demo simulation',
    'access.manage': 'Manage staff access',
  }
  return labels[code] ?? code.replace(/[._]/g, ' ').replace(/\b\w/g, (letter) => letter.toUpperCase())
}

function accessOrder(code: string): number {
  const order = ['changes.publish', 'reports.acknowledge', 'incidents.manage']
  const index = order.indexOf(code)
  return index < 0 ? order.length : index
}
