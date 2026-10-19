/** The part of GET /api/me that decides this. The sign-in code's `Me` fits it; it is not imported so tests stay free of Keycloak. */
type Me = { permissions: { code: string }[] }

// Which workspaces a person sees follows the effective permissions from GET /api/me, never the
// Keycloak role claims. Hiding a link is only a convenience: the API checks every action itself,
// and the shell also shows an access state when someone types a path they may not open.

export type Destination = {
  path: string
  label: string
  /** One honest sentence about what the workspace will hold; nothing is built behind it yet. */
  summary: string
  /** Any one of these permission codes opens the workspace. Empty means any signed-in staff. */
  anyOf: readonly string[]
}

export const destinations: readonly Destination[] = [
  {
    path: '/corridor',
    label: 'Corridor',
    summary: 'Current trains along the Colombo–Kandy corridor.',
    anyOf: ['changes.publish', 'changes.publish_platform', 'incidents.read', 'audit.read', 'simulation.control'],
  },
  {
    path: '/changes',
    label: 'Changes',
    summary: 'Preview, publish and withdraw confirmed train changes.',
    anyOf: ['changes.publish', 'changes.publish_platform'],
  },
  {
    path: '/reports',
    label: 'Reports',
    summary: 'Driver reports waiting for Control.',
    // The raw inbox is Control's. An Auditor reads incidents and history, never the inbox.
    anyOf: ['incidents.manage'],
  },
  {
    path: '/demo-controls',
    label: 'Demo controls',
    summary: 'Pause and resume the simulation.',
    anyOf: ['simulation.control'],
  },
  {
    path: '/account',
    label: 'Account',
    summary: 'Your session and what you can do.',
    anyOf: [],
  },
]

export function canOpen(me: Me, destination: Destination): boolean {
  if (destination.anyOf.length === 0) return true
  const held = new Set(me.permissions.map((p) => p.code))
  return destination.anyOf.some((code) => held.has(code))
}

export function visibleDestinations(me: Me): Destination[] {
  return destinations.filter((d) => canOpen(me, d))
}

/** A journey opened from the corridor: /corridor/journeys/{journeyId}. It needs the Corridor permissions. */
export const journeyPathPattern = /^\/corridor\/journeys\/([^/]+)$/

/** Workspaces whose pages live under their own path: /reports/{reportId}, /incidents/{incidentId}, /audit/... */
const nestedWorkspaces = ['/reports']

export function findDestination(path: string): Destination | undefined {
  const own = destinations.find((d) => d.path === path)
  if (own) return own
  if (journeyPathPattern.test(path)) return destinations.find((d) => d.path === '/corridor')
  return destinations.find((d) => nestedWorkspaces.includes(d.path) && path.startsWith(`${d.path}/`))
}

/** May this person open that path? Used to show a link to another workspace only when it would open. */
export function canOpenPath(me: Me, path: string): boolean {
  const destination = findDestination(path)
  return destination !== undefined && canOpen(me, destination)
}

/**
 * The page group focus belongs to. Moving between pages of one list-and-detail workspace keeps
 * keyboard focus where the person put it; any other move lands on the new page.
 */
export function focusScope(path: string): string {
  const nested = nestedWorkspaces.find((root) => path === root || path.startsWith(`${root}/`))
  return nested ?? path
}

/** Where someone lands when they open the site root. */
export function homeFor(me: Me): string {
  return visibleDestinations(me)[0]?.path ?? '/account'
}
