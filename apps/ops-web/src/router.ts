import { useSyncExternalStore } from 'react'

// A tiny History API router: the shell has a handful of paths, so a routing package is not needed.
// Only the path is kept. It is never a token and never a permission decision.

const listeners = new Set<() => void>()

function subscribe(listener: () => void) {
  listeners.add(listener)
  window.addEventListener('popstate', listener)
  return () => {
    listeners.delete(listener)
    window.removeEventListener('popstate', listener)
  }
}

export function usePath(): string {
  return useSyncExternalStore(subscribe, () => window.location.pathname)
}

export function navigate(to: string, options: { replace?: boolean } = {}) {
  if (to === window.location.pathname) return
  if (options.replace) {
    window.history.replaceState(null, '', to)
  } else {
    window.history.pushState(null, '', to)
  }
  listeners.forEach((listener) => listener())
}

const returnToKey = 'smartrail.returnTo'

/**
 * Keycloak's silent session check and its sign-in page both send the browser back to the site
 * root, so keep the page that was asked for until sign-in finishes. Only the path is stored.
 */
export function rememberDestination() {
  const path = window.location.pathname
  if (path !== '/') window.sessionStorage.setItem(returnToKey, path)
}

export function rememberedDestination(): string | null {
  const path = window.sessionStorage.getItem(returnToKey)
  return path && path.startsWith('/') && !path.startsWith('//') ? path : null
}

export function forgetDestination() {
  window.sessionStorage.removeItem(returnToKey)
}
