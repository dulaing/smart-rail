import { randomBytes } from "node:crypto";

// A staff session is an opaque random id mapped to a collection point. The id is the only thing the
// browser holds (an HttpOnly cookie); everything else lives here, so nothing about the point or the
// password is readable from the page. Sessions are in memory, so a restart signs everyone out.
export interface StaffSession {
  id: string;
  collectionPointId: string;
  csrfToken: string;
  createdAt: number;
  expiresAt: number;
}

const newId = (): string => randomBytes(32).toString("base64url");

// Opaque URL-safe token, for session -> browser values such as the pre-login CSRF token.
export const randomToken = (): string => randomBytes(32).toString("base64url");

export class SessionStore {
  private readonly sessions = new Map<string, StaffSession>();

  // Generate a fresh id and CSRF token for a point. Called on every login, so a reused cookie cannot
  // fixate the session.
  create(collectionPointId: string, now: number, lifetimeMs: number): StaffSession {
    const session: StaffSession = {
      id: newId(),
      collectionPointId,
      csrfToken: newId(),
      createdAt: now,
      expiresAt: now + lifetimeMs,
    };
    this.sessions.set(session.id, session);
    return session;
  }

  // Look a session up and drop it the moment it has expired, so a stale cookie is a miss, not a hit.
  get(id: string, now: number): StaffSession | undefined {
    const session = this.sessions.get(id);
    if (!session) return undefined;
    if (session.expiresAt <= now) {
      this.sessions.delete(id);
      return undefined;
    }
    return session;
  }

  destroy(id: string): void {
    this.sessions.delete(id);
  }

  // Drop every expired session. Called on each login so the map cannot grow without bound.
  sweep(now: number): void {
    for (const [id, session] of this.sessions) {
      if (session.expiresAt <= now) this.sessions.delete(id);
    }
  }
}
