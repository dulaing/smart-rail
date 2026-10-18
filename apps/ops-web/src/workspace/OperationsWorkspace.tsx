import { journeyPathPattern } from '../access'
import type { Me } from '../auth'
import { CorridorOverview } from './CorridorOverview.tsx'
import { JourneyWorkspace } from './JourneyWorkspace.tsx'

/**
 * Corridor, one journey in it, and the list of journeys with confirmed changes. The shell has
 * already checked that this person may open the destination; the API still checks every action.
 */
export function OperationsWorkspace({ me, path, onRefreshMe }: { me: Me; path: string; onRefreshMe: () => void }) {
  const journey = journeyPathPattern.exec(path)
  if (journey) return <JourneyWorkspace me={me} journeyId={journey[1]} onRefreshMe={onRefreshMe} />
  return <CorridorOverview me={me} mode={path === '/changes' ? 'changed' : 'all'} />
}
