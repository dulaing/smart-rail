import { useCallback, useEffect, useState } from 'react'
import { Checking, Expired, NoAccount, SignedOut, Unreachable } from './AuthStates'
import { initAuth, loadAccount, signIn, signOut, type SignInState } from './auth'
import { StatusSample } from './design/StatusSample'
import { forgetDestination, navigate, rememberDestination, rememberedDestination, usePath } from './router'
import { Shell } from './Shell'
import './App.css'

// A deep link survives the round trips to Keycloak, which always return to the site root.
rememberDestination()

type AppState = SignInState | { kind: 'loading' } | { kind: 'init-failed' }

function App() {
  const [state, setState] = useState<AppState>({ kind: 'loading' })
  const path = usePath()

  const load = useCallback(async (forceRefresh = false) => {
    setState(await loadAccount(forceRefresh))
  }, [])

  useEffect(() => {
    let cancelled = false
    initAuth()
      .then((signedIn) => {
        if (cancelled) return
        if (signedIn) {
          // Back from Keycloak: reopen the page that was asked for before signing in.
          const wanted = rememberedDestination()
          if (wanted) navigate(wanted, { replace: true })
          forgetDestination()
          void load()
        } else {
          setState({ kind: 'signed-out' })
        }
      })
      .catch(() => {
        if (!cancelled) setState({ kind: 'init-failed' })
      })
    return () => {
      cancelled = true
    }
  }, [load])

  const startSignIn = () => void signIn()
  const startSignOut = () => {
    forgetDestination()
    void signOut()
  }

  switch (state.kind) {
    case 'loading':
      return <Checking />
    case 'signed-out':
      return <SignedOut onSignIn={startSignIn} />
    case 'expired':
      return <Expired onSignIn={startSignIn} />
    case 'no-account':
      return <NoAccount onSignOut={startSignOut} />
    case 'init-failed':
      // The Keycloak adapter can only be started once per page, so retrying means reloading.
      return (
        <Unreachable
          message="Could not reach the sign-in service."
          onRetry={() => window.location.reload()}
          onSignOut={startSignOut}
        />
      )
    case 'error':
      return (
        <Unreachable
          message={state.message}
          onRetry={() => {
            setState({ kind: 'loading' })
            void load()
          }}
          onSignOut={startSignOut}
        />
      )
    case 'signed-in':
      return (
        <Shell
          me={state.me}
          path={path}
          onRefresh={() => void load(true)}
          onSignOut={startSignOut}
        />
      )
  }
}

// ?design-sample opens the shared status sample in a development build only.
const showSample = import.meta.env.DEV && new URLSearchParams(window.location.search).has('design-sample')

export default function Root() {
  return showSample ? <StatusSample /> : <App />
}
