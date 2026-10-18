import type { Me } from './auth'

export function AccountPage({
  me,
  onRefresh,
  onSignOut,
}: {
  me: Me
  onRefresh: () => void
  onSignOut: () => void
}) {
  return (
    <section aria-label="Your account" className="stack">
      <p>
        Status: <strong>{me.status}</strong>
      </p>
      <div>
        <h2>Roles</h2>
        <ul>
          {me.grants.map((grant) => (
            <li key={`${grant.role}-${grant.stationId ?? 'all'}`}>
              {grant.role}
              {grant.stationId ? ' (one station)' : ''}
              {grant.validUntil ? ` until ${grant.validUntil}` : ''}
            </li>
          ))}
        </ul>
      </div>
      <div>
        <h2>What you can do</h2>
        <ul>
          {me.permissions.map((permission) => (
            <li key={permission.code}>
              <code>{permission.code}</code> — {permission.scope.toLowerCase()}
            </li>
          ))}
        </ul>
      </div>
      <div className="actions">
        <button type="button" onClick={onRefresh}>
          Refresh session
        </button>
        <button type="button" onClick={onSignOut}>
          Sign out
        </button>
      </div>
    </section>
  )
}
