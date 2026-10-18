// The words and symbols for one board row, kept apart from the layout so they can be tested.
// A pill is a status carried by an icon shape and words in all three languages (never colour
// alone). A note is a plain sentence under it. Nothing here names a person or invents a cause:
// the public reads only say what staff decided and when.

import { evidence, statuses, type Evidence, type IconKey, type StatusKey, type Tone } from '../../../shared/design/generated/status.ts'
import type { BoardRow } from './board.ts'
import { say, sayShared, type Texts } from './messages.ts'
import { formatClock } from './time.ts'

export type Pill = { id: string; tone: Tone; icon: IconKey; texts: Texts }

const fromStatus = (key: StatusKey, texts: Texts = statuses[key].text): Pill => ({
  id: key,
  tone: statuses[key].tone,
  icon: statuses[key].icon,
  texts,
})
const fromEvidence = (key: Evidence): Pill => ({ id: key, tone: evidence[key].tone, icon: evidence[key].icon, texts: evidence[key].text })

export type RowText = { pills: Pill[]; notes: Texts[] }

const join = (first: Texts, second: Texts): Texts => ({
  en: `${first.en} · ${second.en}`,
  si: `${first.si} · ${second.si}`,
  ta: `${first.ta} · ${second.ta}`,
})

/** "Staff-confirmed change · 07:42", or nothing when the read gave no confirmation time. */
const confirmedChange = (at: number | null): Texts[] =>
  at === null ? [] : [sayShared('change.confirmed', { time: formatClock(at) })]

/** `station` turns a station ID into its three names. */
export function describeRow(row: BoardRow, station: (id: string) => Texts): RowText {
  // With the whole feed lost the rows are the saved timetable and say nothing about running.
  if (row.timetableOnly) return { pills: [], notes: [] }

  const pills: Pill[] = []
  const notes: Texts[] = []

  if (row.cancelled) {
    pills.push(fromStatus('cancelled'))
    notes.push(...confirmedChange(row.confirmedChangeAt))
  } else if (row.unservedHere) {
    // The train still runs, but no longer to this station, so it must not read as a departure from here.
    pills.push(fromStatus('ends-at-station'))
    if (row.endsAtStationId) notes.push(say('endsAt', { station: station(row.endsAtStationId) }))
    notes.push(say('notStopping'), ...confirmedChange(row.confirmedChangeAt))
  } else {
    if (row.endsEarly) {
      pills.push(fromStatus('ends-at-station'))
      if (row.endsAtStationId) notes.push(say('endsAt', { station: station(row.endsAtStationId) }))
      notes.push(...confirmedChange(row.confirmedChangeAt))
    }
    if (row.delayed) {
      pills.push(fromStatus('delayed', join(statuses.delayed.text, say('delayMinutes', { n: row.delayMinutes ?? 0 }))))
      if (row.confirmedDelay) {
        // Only the delay is public, never a cause: say staff confirmed it, and when if that is known.
        notes.push(
          row.confirmedDelay.at === null
            ? say('confirmedDelayPlain')
            : say('confirmedDelay', { time: formatClock(row.confirmedDelay.at) }),
        )
      }
    } else if (row.onTime) {
      pills.push(fromStatus('on-time'))
    }
    if (row.evidence !== 'CURRENT') pills.push(fromEvidence(row.evidence))
    if (row.moved) {
      pills.push(fromStatus('platform-change'))
      const moved = say('moved', { from: row.moved.from, to: row.moved.to })
      notes.push(row.moved.confirmedAt === null ? moved : join(moved, sayShared('change.confirmed', { time: formatClock(row.moved.confirmedAt) })))
    }
  }

  if (row.refreshFailed) {
    notes.push(say('rowFailed', { time: row.checkedAt === null ? '--:--' : formatClock(row.checkedAt) }))
  }
  return { pills, notes }
}
