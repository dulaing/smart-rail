import { useEffect, useState } from 'react'
import { publicReader } from '../workspace/live.ts'
import { loadJourneyChoices, type JourneyChoice } from './journeyChoices.ts'

/**
 * The recent journeys, read once when the page opens, for labels and the journey filter. A failed
 * read leaves the list empty and every journey shows a short ID instead; it blocks nothing.
 */
export function useJourneyChoices(): { choices: JourneyChoice[]; failed: boolean } {
  const [state, setState] = useState<{ choices: JourneyChoice[]; failed: boolean }>({ choices: [], failed: false })
  useEffect(() => {
    let cancelled = false
    void loadJourneyChoices(publicReader, Date.now()).then((result) => {
      if (!cancelled) setState(result)
    })
    return () => {
      cancelled = true
    }
  }, [])
  return state
}
