import type { ActiveChange, ChangeKind } from './types.ts'

/** The part of GET /api/me that decides this. The sign-in code's `Me` fits it; it is not imported so tests stay free of Keycloak. */
type Me = { permissions: { code: string; scope: string; stationIds: string[] }[] }

// What this person may change, read from the effective permissions in GET /api/me. This only
// decides which controls to show. The API checks every preview and confirmation itself, against
// the database, so an out-of-date list here can show a control that the server then refuses.

export type ChangeRights = {
  /** changes.publish: any change on any journey. */
  network: boolean
  /** changes.publish_platform: platform changes at these stations only. */
  platformStationIds: ReadonlySet<string>
  /** incidents.manage: may name an open incident on a change. */
  linkIncident: boolean
}

export function changeRights(me: Me): ChangeRights {
  const platformStationIds = new Set<string>()
  let network = false
  let linkIncident = false
  for (const permission of me.permissions) {
    if (permission.code === 'changes.publish') network = true
    if (permission.code === 'incidents.manage') linkIncident = true
    if (permission.code === 'changes.publish_platform') {
      permission.stationIds.forEach((id) => platformStationIds.add(id))
    }
  }
  return { network, platformStationIds, linkIncident }
}

export function canChange(rights: ChangeRights): boolean {
  return rights.network || rights.platformStationIds.size > 0
}

/** The change types this person may start. Station staff only ever see PLATFORM. */
export function startableKinds(rights: ChangeRights): ChangeKind[] {
  if (rights.network) return ['DELAY', 'CANCEL', 'PLATFORM', 'EARLY_END']
  return rights.platformStationIds.size > 0 ? ['PLATFORM'] : []
}

/** May a platform change be started at a stop of this station? */
export function canChangePlatformAt(rights: ChangeRights, stationId: string): boolean {
  return rights.network || rights.platformStationIds.has(stationId)
}

/** May this person withdraw this active change? `targetStationId` is the station of a platform change's stop. */
export function canWithdraw(rights: ChangeRights, change: ActiveChange, targetStationId: string | null): boolean {
  if (rights.network) return true
  return change.kind === 'PLATFORM' && targetStationId !== null && rights.platformStationIds.has(targetStationId)
}

/**
 * Which stations' journeys to list: null for everyone who can see the whole corridor, otherwise
 * the stations a station-scoped account was granted. The reads themselves are public, so this
 * narrows the list for convenience; it is not what keeps anything private.
 */
export function corridorStations(me: Me): ReadonlySet<string> | null {
  const wholeCorridor = me.permissions.some(
    (p) => p.scope === 'NETWORK' && ['changes.publish', 'incidents.read', 'audit.read', 'simulation.control'].includes(p.code),
  )
  if (wholeCorridor) return null
  return changeRights(me).platformStationIds
}
