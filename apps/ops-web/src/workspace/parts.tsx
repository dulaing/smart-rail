import type { MouseEvent, ReactNode } from 'react'
import type { Evidence } from '../../../../shared/design/generated/status.ts'
import { StatusBadge } from '../design/StatusBadge.tsx'
import { navigate } from '../router.ts'
import { hasConfirmedChange, type ConfirmedFacts, type DelayFact } from './facts.ts'
import './workspace.css'

/** A link that moves inside the app without reloading, like the navigation in the shell. */
export function AppLink({ to, children, label }: { to: string; children: ReactNode; label?: string }) {
  const go = (event: MouseEvent<HTMLAnchorElement>) => {
    // Let the browser handle new-tab and new-window clicks.
    if (event.metaKey || event.ctrlKey || event.shiftKey || event.button !== 0) return
    event.preventDefault()
    navigate(to)
  }
  return (
    <a href={to} onClick={go} aria-label={label}>
      {children}
    </a>
  )
}

/** The delay the evidence supports, or an explicit "not known". Never "on time" without evidence. */
export function DelayCell({ delay, evidence }: { delay: DelayFact; evidence: Evidence }) {
  if (delay.kind === 'unknown') {
    return <span className="muted">{evidence === 'UNKNOWN' ? 'Not known' : 'No delay reported'}</span>
  }
  if (delay.kind === 'on-time') return <StatusBadge status="on-time" locale="en" />
  return (
    <span className="badges">
      <StatusBadge status="delayed" locale="en" />
      <span>{delay.minutes} min late</span>
    </span>
  )
}

/** What staff have confirmed, separate from what was observed. */
export function ConfirmedCell({ facts }: { facts: ConfirmedFacts }) {
  if (!hasConfirmedChange(facts)) return <span className="muted">No confirmed changes</span>
  return (
    <span className="badges">
      {facts.availability === 'CANCELLED' && <StatusBadge status="cancelled" locale="en" />}
      {facts.availability === 'ENDS_EARLY' && (
        <>
          <StatusBadge status="ends-at-station" locale="en" />
          {facts.endsAt && <span>at {facts.endsAt}</span>}
        </>
      )}
      {facts.delayMinutes !== null && (
        <>
          <StatusBadge status="delayed" locale="en" />
          <span>{facts.delayMinutes} min confirmed</span>
        </>
      )}
      {facts.platformChanges > 0 && <StatusBadge status="platform-change" locale="en" />}
    </span>
  )
}
