import { useEffect, useRef } from 'react'
import { AccountPage } from './AccountPage'
import { canOpen, findDestination, focusScope, homeFor, visibleDestinations } from './access'
import type { Me } from './auth'
import { messages } from '../../../shared/design/generated/status.ts'
import { navigate } from './router'
import { AuditWorkspace } from './reports/AuditWorkspace'
import { IncidentWorkspace } from './reports/IncidentWorkspace'
import { ReportsWorkspace } from './reports/ReportsWorkspace'
import { OperationsWorkspace } from './workspace/OperationsWorkspace'
import operationsIcon from '../../../shared/design/logo/operations-icon.png'

export function Shell({
  me,
  path,
  onRefresh,
  onSignOut,
}: {
  me: Me
  path: string
  onRefresh: () => void
  onSignOut: () => void
}) {
  const mainRef = useRef<HTMLElement>(null)
  const lastScope = useRef(focusScope(path))

  // The site root opens the first workspace this person may use.
  useEffect(() => {
    if (path === '/') navigate(homeFor(me), { replace: true })
  }, [me, path])

  const destination = findDestination(path)
  const allowed = destination ? canOpen(me, destination) : false
  const title = pageTitle(path, destination?.label)

  // After moving between workspaces, put keyboard and screen-reader focus on the new page.
  useEffect(() => {
    document.title = `${title} · SmartRail Operations`
    // Only a real change of page moves focus; the first render leaves it at the top of the page.
    const scope = focusScope(path)
    if (lastScope.current !== scope) {
      lastScope.current = scope
      mainRef.current?.focus()
    }
  }, [title, path])

  const links = visibleDestinations(me)
  const navLinks = links.filter((link) => link.path !== '/account' && link.path !== '/demo-controls')
  const role = roleName(me)
  const context = pageContext(path)
  const isController = me.permissions.some((permission) => permission.code === 'changes.publish' && permission.scope === 'NETWORK')
  const isAuditor = me.permissions.some((permission) => permission.code === 'audit.read')
  const workspaceName = isController
    ? 'Control workspace'
    : isAuditor ? 'Read only' : 'Staff workspace'
  const roleContext = isController ? 'pilot network' : isAuditor ? 'read only' : 'staff workspace'

  return (
    <div className="shell">
      <a className="skip" href="#main">
        Skip to content
      </a>
      <aside className="sidebar">
        <a className="sidebar-brand" href="/corridor" onClick={(event) => { event.preventDefault(); navigate('/corridor') }}>
          <img src={operationsIcon} alt="" />
          <span className="sidebar-brand-copy">
            <span>SmartRail</span>
            <small>OPERATIONS</small>
          </span>
        </a>
        <nav className="nav" aria-label="Workspaces">
          <ul>
            {navLinks.map((link) => {
              const label = workspaceLabel(link.path, link.label)
              const active = link.path === destination?.path || (link.path === '/corridor' && path.startsWith('/corridor/'))
              return (
                <li key={link.path}>
                  <a
                    href={link.path}
                    aria-current={active ? 'page' : undefined}
                    onClick={(event) => {
                      event.preventDefault()
                      navigate(link.path)
                    }}
                  >
                    <WorkspaceIcon path={link.path} />
                    <span>{label}</span>
                  </a>
                </li>
              )
            })}
          </ul>
        </nav>
        <div className="sidebar-bottom">
          <div className="sidebar-account">
            <span>{workspaceName}</span>
            <strong>{me.displayName}</strong>
          </div>
          <a
            className="account-link"
            href="/account"
            aria-current={path === '/account' ? 'page' : undefined}
            onClick={(event) => { event.preventDefault(); navigate('/account') }}
          >
            Staff account
          </a>
        </div>
      </aside>
      <header className="topbar">
        <span className="section-name">{context}</span>
        <span className="sr-badge sr-tone-neutral">{messages['footer.simulation'].text.en}</span>
        <time className="shell-clock" dateTime={new Date().toISOString()}>{colomboClock()}</time>
        <span className="who">
          <span>{me.displayName} · {role} · {roleContext}</span>
        </span>
      </header>

      <main id="main" tabIndex={-1} ref={mainRef} className="content">
        <div className="page-title-row">
          <h1>{title}</h1>
          {path === '/corridor' && (
            <a className="compose-link" href="#corridor-train-list" aria-label="Choose a train to compose a change">
              Compose change
            </a>
          )}
          {path === '/account' && <button className="title-action" type="button" onClick={onSignOut}>Sign out</button>}
        </div>
        {path === '/' && <p role="status">Opening your workspace…</p>}
        {path !== '/' && !destination && (
          <section className="notice">
            <p role="alert">There is no page at this address.</p>
            <a href={homeFor(me)} onClick={(e) => { e.preventDefault(); navigate(homeFor(me)) }}>
              Go to your workspace
            </a>
          </section>
        )}
        {destination && !allowed && (
          <section className="notice">
            <p role="alert">
              Your account does not have access to {destination.label.toLowerCase()}. The server
              also checks every action, so this page cannot be opened by typing the address.
            </p>
            <a href={homeFor(me)} onClick={(e) => { e.preventDefault(); navigate(homeFor(me)) }}>
              Go to your workspace
            </a>
          </section>
        )}
        {destination && allowed && destination.path === '/account' && (
          <AccountPage me={me} onRefresh={onRefresh} />
        )}
        {destination && allowed && (destination.path === '/corridor' || destination.path === '/changes') && (
          <OperationsWorkspace me={me} path={path} onRefreshMe={onRefresh} />
        )}
        {destination && allowed && destination.path === '/reports' && (
          <ReportsWorkspace me={me} path={path} onRefreshMe={onRefresh} />
        )}
        {destination && allowed && destination.path === '/incidents' && (
          <IncidentWorkspace me={me} path={path} onRefreshMe={onRefresh} />
        )}
        {destination && allowed && destination.path === '/audit' && (
          <AuditWorkspace me={me} path={path} onRefreshMe={onRefresh} />
        )}
        {destination && allowed && !['/account', '/corridor', '/changes', '/reports', '/incidents', '/audit'].includes(destination.path) && (
          <section className="notice neutral">
            <h2>Not available yet</h2>
            <p>{destination.summary}</p>
          </section>
        )}
      </main>
    </div>
  )
}

function roleName(me: Me): string {
  const roles = [...new Set(me.grants.map((grant) => grant.role))]
  if (roles.length === 0) return 'Staff'
  return roles.map((role) => role.toLowerCase().split('_').map((part) => `${part[0]?.toUpperCase() ?? ''}${part.slice(1)}`).join(' ')).join(', ')
}

function pageContext(path: string): string {
  if (path === '/account') return 'Trains'
  if (path === '/corridor' || path.startsWith('/corridor/')) return 'Trains'
  if (path === '/changes') return 'Changes'
  if (path === '/reports' || path.startsWith('/reports/')) return 'Driver reports'
  if (path === '/incidents' || path.startsWith('/incidents/')) return 'Incidents'
  if (path.startsWith('/audit/journeys')) return 'Journey history'
  if (path.startsWith('/audit/incidents')) return 'Incident history'
  return 'Operations'
}

function pageTitle(path: string, fallback: string | undefined): string {
  if (path === '/') return 'SmartRail Operations'
  if (path === '/corridor') return 'Pilot corridor'
  if (path.startsWith('/corridor/')) return 'Train details'
  if (path === '/reports') return 'Driver reports'
  if (path.startsWith('/reports/')) return 'Driver report'
  if (path.startsWith('/incidents/')) return 'Incident details'
  if (path === '/account') return 'Staff account'
  if (path.startsWith('/audit/incidents')) return 'Incident history'
  if (path.startsWith('/audit')) return 'Journey history'
  return fallback ?? 'Page not found'
}

function colomboClock(): string {
  return new Intl.DateTimeFormat('en-LK', {
    hour: 'numeric',
    minute: '2-digit',
    hour12: true,
    timeZone: 'Asia/Colombo',
  }).format(new Date())
}

function workspaceLabel(path: string, fallback: string): string {
  if (path === '/corridor') return 'Trains'
  if (path === '/reports') return 'Driver reports'
  if (path === '/audit') return 'Journey history'
  return fallback
}

function WorkspaceIcon({ path }: { path: string }) {
  if (path === '/corridor') {
    return <svg className="nav-icon" viewBox="0 0 24 24" aria-hidden="true"><rect x="5" y="3" width="14" height="15" rx="2" /><path d="M8 7h8M8 11h3m5 0h-1M8 18l-2 3m10-3 2 3M7 15h10" /><circle cx="8" cy="16" r=".7" /><circle cx="16" cy="16" r=".7" /></svg>
  }
  if (path === '/reports') {
    return <svg className="nav-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 21V4m1 1c5-4 8 3 13-1v10c-5 4-8-3-13 1" /></svg>
  }
  if (path === '/incidents') {
    return <svg className="nav-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3 2.8 20h18.4L12 3Z" /><path d="M12 9v5m0 3h.01" /></svg>
  }
  return <svg className="nav-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01" /></svg>
}
