import { describeFreshness, type DescribeFreshnessInput } from '../../../../shared/design/freshness.ts'
import { StatusIcon } from './StatusBadge.tsx'
import './design.css'

/**
 * One line saying how fresh the simulated running information is. It is given every input and
 * fetches nothing, polls nothing and owns no cache. `now` must already be corrected with server time.
 */
export function FreshnessLine(props: DescribeFreshnessInput) {
  const description = describeFreshness(props)
  return (
    <span className={`sr-freshness sr-tone-${description.tone}`} lang={props.locale}>
      <StatusIcon name={description.icon} />
      <span>{description.text}</span>
    </span>
  )
}
