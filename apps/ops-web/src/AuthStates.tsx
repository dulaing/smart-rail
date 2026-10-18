import type { ReactNode } from 'react'

// The screens shown before a person is signed in. Staff apps stay protected while the session is
// being checked, so nothing from the shell is visible here.

function Frame({ title, children }: { title: string; children: ReactNode }) {
  return (
    <main className="gate" id="main">
      <p className="brand">SmartRail Operations</p>
      <section className="gate-card" aria-labelledby="gate-title">
        <h1 id="gate-title">{title}</h1>
        {children}
      </section>
    </main>
  )
}

export function Checking() {
  return (
    <Frame title="Checking your session">
      <p role="status">One moment…</p>
    </Frame>
  )
}

export function SignedOut({ onSignIn }: { onSignIn: () => void }) {
  return (
    <Frame title="Staff sign-in">
      <p>Sign in with your SmartRail staff account.</p>
      <div className="actions">
        <button type="button" className="primary" onClick={onSignIn}>
          Sign in
        </button>
      </div>
    </Frame>
  )
}

export function Expired({ onSignIn }: { onSignIn: () => void }) {
  return (
    <Frame title="Session ended">
      <p role="alert">Your session has expired. Sign in again to continue.</p>
      <div className="actions">
        <button type="button" className="primary" onClick={onSignIn}>
          Sign in
        </button>
      </div>
    </Frame>
  )
}

export function NoAccount({ onSignOut }: { onSignOut: () => void }) {
  return (
    <Frame title="No SmartRail account">
      <p role="alert">
        You are signed in, but there is no active SmartRail account for you. Ask a system
        administrator to enable one.
      </p>
      <div className="actions">
        <button type="button" onClick={onSignOut}>
          Sign out
        </button>
      </div>
    </Frame>
  )
}

export function Unreachable({
  message,
  onRetry,
  onSignOut,
}: {
  message: string
  onRetry: () => void
  onSignOut: () => void
}) {
  return (
    <Frame title="Could not connect">
      <p role="alert">{message} This is a connection problem, not a permission problem.</p>
      <div className="actions">
        <button type="button" className="primary" onClick={onRetry}>
          Try again
        </button>
        <button type="button" onClick={onSignOut}>
          Sign out
        </button>
      </div>
    </Frame>
  )
}
