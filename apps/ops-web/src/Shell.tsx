import { useEffect, useRef } from 'react'
import { AccountPage } from './AccountPage'
import { canOpen, findDestination, focusScope, homeFor, visibleDestinations } from './access'
import type { Me } from './auth'
import { messages } from '../../../shared/design/generated/status.ts'
import { navigate } from './router'
import { IncidentWorkspace } from './reports/IncidentWorkspace'
import { ReportsWorkspace } from './reports/ReportsWorkspace'
import { OperationsWorkspace } from './workspace/OperationsWorkspace'

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
  const title = path === '/' ? 'SmartRail Operations' : (destination?.label ?? 'Page not found')

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

  return (
    <div className="shell">
      <a className="skip" href="#main">
        Skip to content
      </a>
      <header className="topbar">
        <span className="brand">SmartRail Operations</span>
        <span className="sr-badge sr-tone-neutral">{messages['footer.simulation'].text.en}</span>
        <span className="who">
          <span>{me.displayName}</span>
          <button type="button" onClick={onSignOut}>
            Sign out
          </button>
        </span>
      </header>

      <nav className="nav" aria-label="Workspaces">
        <ul>
          {links.map((link) => (
            <li key={link.path}>
              <a
                href={link.path}
                aria-current={link.path === destination?.path ? 'page' : undefined}
                onClick={(event) => {
                  event.preventDefault()
                  navigate(link.path)
                }}
              >
                {link.label}
              </a>
            </li>
          ))}
        </ul>
      </nav>

      <main id="main" tabIndex={-1} ref={mainRef} className="content">
        <h1>{title}</h1>
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
          <AccountPage me={me} onRefresh={onRefresh} onSignOut={onSignOut} />
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
        {destination && allowed && !['/account', '/corridor', '/changes', '/reports', '/incidents'].includes(destination.path) && (
          <section className="notice neutral">
            <h2>Not available yet</h2>
            <p>{destination.summary}</p>
          </section>
        )}
      </main>
    </div>
  )
}
