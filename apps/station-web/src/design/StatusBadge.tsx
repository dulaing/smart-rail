import { evidence, icons, statuses, type Evidence, type IconKey, type Locale, type StatusKey } from '../../../../shared/design/generated/status.ts'
import './design.css'

export function StatusIcon({ name }: { name: IconKey }) {
  return (
    <svg className="sr-icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <path d={icons[name]} />
    </svg>
  )
}

/**
 * A status or evidence label: text plus an icon shape on a tinted background, so colour never
 * carries the meaning alone. It only displays what it is given and never works out a state.
 */
export function StatusBadge({ status, locale }: { status: StatusKey | Evidence; locale: Locale }) {
  const def = status in statuses ? statuses[status as StatusKey] : evidence[status as Evidence]
  return (
    <span className={`sr-badge sr-tone-${def.tone}`} lang={locale}>
      <StatusIcon name={def.icon} />
      <span>{def.text[locale]}</span>
    </span>
  )
}
