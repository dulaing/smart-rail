// Small pieces the two boards share: the three languages together, a status pill and the arrow
// that says whether a row is a departure or an arrival.

import { locales } from '../../../shared/design/generated/status.ts'
import { StatusIcon } from './design/StatusBadge.tsx'
import type { Pill } from './rowText.ts'
import type { Texts } from './messages.ts'

/** English, Sinhala and Tamil together at one size, wrapping to complete lines. Never a carousel. */
export function Lines({ texts, inline = false }: { texts: Texts; inline?: boolean }) {
  return (
    <span className={inline ? 'lines lines-inline' : 'lines'}>
      {locales.map((locale) => (
        <span key={locale} lang={locale}>
          {texts[locale]}
        </span>
      ))}
    </span>
  )
}

/** A status: icon shape plus its words in all three languages. */
export function StatusPill({ pill }: { pill: Pill }) {
  return (
    <span className={`pill sr-tone-${pill.tone}`}>
      <StatusIcon name={pill.icon} />
      <Lines texts={pill.texts} />
    </span>
  )
}

const arrows = {
  departure: 'M5 12h14M13 6l6 6-6 6',
  arrival: 'M19 12H5M11 6l-6 6 6 6',
} as const

export function ArrowIcon({ kind }: { kind: 'departure' | 'arrival' }) {
  return (
    <svg className="sr-icon arrow" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <path d={arrows[kind]} />
    </svg>
  )
}
