// Which station and view a screen shows. It comes from the page address, for example
//   /?station=GPH&view=platform&platform=2     one platform's next train
//   /?station=FOT&view=entrance                the entrance overview (also used in waiting areas)
// with VITE_STATION, VITE_VIEW and VITE_PLATFORM as defaults for a screen that always opens the
// same URL. `station` is a station ID or a station code. There is no fallback station: a missing
// or unknown value is a configuration error shown on the screen, never another station.

import type { Journey, Station } from './api.ts'

export type BoardView = 'entrance' | 'platform'

export type BoardConfig = { station: string; view: BoardView; platform: string | null }

export type ConfigProblem = 'missing-station' | 'bad-view' | 'missing-platform' | 'unknown-station' | 'unknown-platform'

export type ConfigResult = { ok: true; config: BoardConfig } | { ok: false; problem: ConfigProblem }

type Defaults = { station?: string; view?: string; platform?: string }

/** Checks what can be checked without the station list. */
export function parseConfig(search: string, defaults: Defaults = {}): ConfigResult {
  const query = new URLSearchParams(search)
  const station = (query.get('station') ?? defaults.station ?? '').trim()
  const view = (query.get('view') ?? defaults.view ?? 'entrance').trim()
  const platform = (query.get('platform') ?? defaults.platform ?? '').trim()

  if (!station) return { ok: false, problem: 'missing-station' }
  if (view !== 'entrance' && view !== 'platform') return { ok: false, problem: 'bad-view' }
  if (view === 'platform' && !platform) return { ok: false, problem: 'missing-platform' }
  return { ok: true, config: { station, view, platform: view === 'platform' ? platform : null } }
}

/** The station a configuration names, by ID or code, or undefined. */
export function findStation(stations: readonly Station[], reference: string): Station | undefined {
  const wanted = reference.toLowerCase()
  return stations.find((s) => s.id.toLowerCase() === wanted || s.code.toLowerCase() === wanted)
}

/** The platforms the timetable plans at a station. A platform outside this set is a configuration error. */
export function platformsAt(journeys: readonly Journey[], stationId: string): Set<string> {
  const platforms = new Set<string>()
  for (const journey of journeys) {
    for (const stop of journey.stops) {
      if (stop.stationId === stationId && stop.plannedPlatform) platforms.add(stop.plannedPlatform)
    }
  }
  return platforms
}

/**
 * Checks the configuration against what is known. With no timetable at all the platform cannot be
 * judged, so it passes until a timetable arrives rather than reporting a false error.
 */
export function validateConfig(
  config: BoardConfig,
  stations: readonly Station[],
  journeys: readonly Journey[],
): { ok: true; station: Station } | { ok: false; problem: ConfigProblem } {
  const station = findStation(stations, config.station)
  if (!station) return { ok: false, problem: 'unknown-station' }
  if (config.view === 'platform' && journeys.length > 0 && !platformsAt(journeys, station.id).has(config.platform ?? '')) {
    return { ok: false, problem: 'unknown-platform' }
  }
  return { ok: true, station }
}
