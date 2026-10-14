import { useCallback, useEffect, useState } from 'react'
import { initAuth, loadAccount, signIn, signOut, type SignInState } from './auth'
import './App.css'

function App() {
  const [state, setState] = useState<SignInState | 'loading'>('loading')

  const load = useCallback(async (forceRefresh = false) => {
    setState(await loadAccount(forceRefresh))
  }, [])

  useEffect(() => {
    let cancelled = false
    initAuth()
      .then((signedIn) => {
        if (cancelled) return
        if (signedIn) {
          void load()
        } else {
          setState({ kind: 'signed-out' })
        }
      })
      .catch(() => {
        if (!cancelled) {
          setState({ kind: 'error', message: 'Could not reach the sign-in service.' })
        }
      })
    return () => {
      cancelled = true
    }
  }, [load])

  return (
    <main>
      <h1>SmartRail operations</h1>
      {state === 'loading' && <p>Checking your session…</p>}

      {state !== 'loading' && state.kind === 'signed-out' && (
        <section>
          <p>Sign in with your SmartRail staff account.</p>
          <button type="button" onClick={() => void signIn()}>
            Sign in
          </button>
        </section>
      )}

      {state !== 'loading' && state.kind === 'no-account' && (
        <section>
          <p>
            You are signed in, but there is no active SmartRail account for you. Ask a
            system administrator to enable one.
          </p>
          <button type="button" onClick={() => void signOut()}>
            Sign out
          </button>
        </section>
      )}

      {state !== 'loading' && state.kind === 'error' && (
        <section>
          <p role="alert">{state.message}</p>
          <button type="button" onClick={() => void load()}>
            Try again
          </button>{' '}
          <button type="button" onClick={() => void signOut()}>
            Sign out
          </button>
        </section>
      )}

      {state !== 'loading' && state.kind === 'signed-in' && (
        <section aria-label="Your account">
          <h2>{state.me.displayName}</h2>
          <p>
            Status: <strong>{state.me.status}</strong>
          </p>
          <h3>Roles</h3>
          <ul>
            {state.me.grants.map((grant) => (
              <li key={`${grant.role}-${grant.stationId ?? 'all'}`}>
                {grant.role}
                {grant.stationId ? ' (one station)' : ''}
                {grant.validUntil ? ` until ${grant.validUntil}` : ''}
              </li>
            ))}
          </ul>
          <h3>What you can do</h3>
          <ul>
            {state.me.permissions.map((permission) => (
              <li key={permission.code}>
                <code>{permission.code}</code> — {permission.scope.toLowerCase()}
              </li>
            ))}
          </ul>
          <button type="button" onClick={() => void load(true)}>
            Refresh session
          </button>{' '}
          <button type="button" onClick={() => void signOut()}>
            Sign out
          </button>
        </section>
      )}
    </main>
  )
}

export default App
