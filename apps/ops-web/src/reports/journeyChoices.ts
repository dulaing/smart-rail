import { mapLimited, type Reader } from '../workspace/api.ts'
import { addDays, colomboDate } from '../workspace/time.ts'
import type { JourneysResponse, ScheduledJourney } from '../workspace/types.ts'

// A report or incident names a journey by ID only. This turns the public timetable of the
// recent service dates into short labels ("Train 1015 · 2026-10-19 · GPH–FOT") and a list to
// filter by. A journey outside those dates keeps a plain shortened ID rather than a guess.

export type JourneyChoice = { journeyId: string; label: string; serviceDate: string }

/** Yesterday and today: a train that left yesterday can still be running after midnight. */
export const choiceDatesBack = 1

export function labelOf(journey: ScheduledJourney): string {
  const first = journey.stops[0]?.stationCode
  const last = journey.stops[journey.stops.length - 1]?.stationCode
  const route = first && last ? ` · ${first}–${last}` : ''
  return `Train ${journey.trainNumber} · ${journey.serviceDate}${route}`
}

export async function loadJourneyChoices(
  reader: Reader,
  now: number,
  signal?: AbortSignal,
): Promise<{ choices: JourneyChoice[]; failed: boolean }> {
  const today = colomboDate(now)
  const dates = Array.from({ length: choiceDatesBack + 1 }, (_, i) => addDays(today, i - choiceDatesBack))
  const reads = await mapLimited(dates, 2, (date) => reader.read<JourneysResponse>(`/api/journeys?serviceDate=${date}`, { signal }))
  const choices = reads.flatMap((read) =>
    read.kind === 'ok'
      ? read.body.journeys.map((j) => ({ journeyId: j.journeyId, label: labelOf(j), serviceDate: j.serviceDate }))
      : [],
  )
  return { choices, failed: reads.some((read) => read.kind === 'failed') }
}

/** The label for a journey ID, or a short, honest stand-in when the journey is not in the list. */
export function journeyLabel(choices: readonly JourneyChoice[], journeyId: string): string {
  return choices.find((c) => c.journeyId === journeyId)?.label ?? `Journey ${journeyId.slice(0, 8)}`
}
