import Keycloak from 'keycloak-js'

// Keycloak owns the credentials, sessions and tokens. This is a public client: the browser
// signs in with Authorization Code + PKCE and holds the tokens in memory only, so a page
// reload starts from a silent session check instead of reading anything from storage.
const keycloak = new Keycloak({
  url: import.meta.env.VITE_KEYCLOAK_URL ?? 'https://localhost:8080',
  realm: import.meta.env.VITE_KEYCLOAK_REALM ?? 'smartrail',
  clientId: import.meta.env.VITE_KEYCLOAK_CLIENT_ID ?? 'smartrail-ops-web',
})

const apiUrl = import.meta.env.VITE_API_URL ?? 'https://localhost:7074'

// The redirect URI is registered exactly in the realm, so send exactly that.
const appUrl = `${window.location.origin}/`

export type Me = {
  accountId: string
  displayName: string
  status: string
  grants: { role: string; stationId: string | null; validUntil: string | null }[]
  permissions: { code: string; scope: string; stationIds: string[]; journeyIds: string[] }[]
}

export type SignInState =
  | { kind: 'signed-out' }
  | { kind: 'signed-in'; me: Me }
  // The credentials were rejected or can no longer be refreshed: sign in again.
  | { kind: 'expired' }
  // Signed in with Keycloak, but the API says there is no enabled SmartRail account.
  | { kind: 'no-account' }
  // The sign-in service or API could not be reached: retry. This is never a permission denial.
  | { kind: 'error'; message: string }

let initialised: Promise<boolean> | undefined

/** Completes a login redirect or restores an existing Keycloak session; true when signed in. */
export function initAuth(): Promise<boolean> {
  initialised ??= keycloak.init({
    onLoad: 'check-sso',
    pkceMethod: 'S256',
    // Third-party cookies are not needed: the session check is a full-page redirect.
    checkLoginIframe: false,
    redirectUri: appUrl,
  })
  return initialised
}

export function signIn() {
  return keycloak.login({ redirectUri: appUrl })
}

/** Clears the in-memory tokens and ends the Keycloak session, then returns to the app. */
export function signOut() {
  return keycloak.logout({ redirectUri: appUrl })
}

/** Reads the caller's local account. Always sends an access token, never the ID token. */
export async function loadAccount(forceRefresh = false): Promise<SignInState> {
  try {
    // A negative minimum validity refreshes now; otherwise refresh only when about to expire.
    await keycloak.updateToken(forceRefresh ? -1 : 30)
  } catch {
    // The refresh failed. With an expired token the session is over; otherwise the sign-in
    // service is probably just unreachable, so offer a retry instead.
    return keycloak.isTokenExpired()
      ? { kind: 'expired' }
      : { kind: 'error', message: 'Could not reach the sign-in service.' }
  }
  try {
    const response = await fetch(`${apiUrl}/api/me`, {
      headers: { Authorization: `Bearer ${keycloak.token}` },
    })
    if (response.ok) {
      return { kind: 'signed-in', me: (await response.json()) as Me }
    }
    if (response.status === 401) {
      return { kind: 'expired' }
    }
    if (response.status === 403) {
      return { kind: 'no-account' }
    }
    return { kind: 'error', message: `The API answered ${response.status}.` }
  } catch {
    return { kind: 'error', message: 'Could not reach the API.' }
  }
}
