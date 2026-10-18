import type { Me } from './auth'

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
    summary: 'Driver reports and incidents.',
    anyOf: ['incidents.read', 'incidents.manage'],
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

export function findDestination(path: string): Destination | undefined {
  return destinations.find((d) => d.path === path)
}

/** Where someone lands when they open the site root. */
export function homeFor(me: Me): string {
  return visibleDestinations(me)[0]?.path ?? '/account'
}
