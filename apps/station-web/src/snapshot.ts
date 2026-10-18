// The small public cache that lets the board reload offline: station names and the timetable for a
// few service dates, each with the time it was downloaded. It holds nothing else. No running
// information, no platform overrides, no confirmed changes, no tokens: an old estimate or
// override must never come back from here looking current. Bump `version` when the shape changes;
// an unknown version is simply thrown away.

import type { Journey, Station } from './api.ts'

export const snapshotKey = 'smartrail.station-board'
export const snapshotVersion = 1

export type SavedDay = {
  serviceDate: string
  /** Device time when this date's timetable was last downloaded in full. */
  downloadedAt: number
  journeys: Journey[]
}

export type Snapshot = {
  version: typeof snapshotVersion
  stations: Station[]
  stationsSavedAt: number
  days: Record<string, SavedDay>
}

type Store = Pick<Storage, 'getItem' | 'setItem'>

export const emptySnapshot = (): Snapshot => ({ version: snapshotVersion, stations: [], stationsSavedAt: 0, days: {} })

/** The saved snapshot, or null if there is none or it cannot be trusted. */
export function loadSnapshot(store: Store): Snapshot | null {
  try {
    const raw = store.getItem(snapshotKey)
    if (!raw) return null
    const parsed = JSON.parse(raw) as Partial<Snapshot> | null
    if (!parsed || parsed.version !== snapshotVersion) return null
    if (!Array.isArray(parsed.stations) || typeof parsed.days !== 'object' || parsed.days === null) return null
    return { ...emptySnapshot(), ...parsed } as Snapshot
  } catch {
    return null
  }
}

export function saveSnapshot(store: Store, snapshot: Snapshot): void {
  try {
    store.setItem(snapshotKey, JSON.stringify(snapshot))
  } catch {
    // Storage can be full or switched off. The board still works from memory without it.
  }
}

/** Drops dates older than yesterday: only a train still running past midnight needs them. */
export function pruneSnapshot(snapshot: Snapshot, oldestDate: string): Snapshot {
  const days = Object.fromEntries(Object.entries(snapshot.days).filter(([date]) => date >= oldestDate))
  return { ...snapshot, days }
}

export const journeysOf = (snapshot: Snapshot): Journey[] => Object.values(snapshot.days).flatMap((day) => day.journeys)
