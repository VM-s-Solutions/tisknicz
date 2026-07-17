import { accessCookieName, type Audience } from './session';

/**
 * Display-only view of the signed-in principal, derived server-side from
 * the audience-scoped access-JWT cookie (ADR 0012). Used to render
 * session-aware chrome (navbar account menu, dashboard navigation).
 *
 * IMPORTANT: the JWT payload is decoded WITHOUT signature verification —
 * the frontend has no signing key and never needs one. Nothing
 * authorization-bearing may branch on this value: the backend validates
 * the signature + audience on every API call, and the edge middleware
 * gates the dashboard routes. A forged cookie can at most render a fake
 * email string in the user's own browser.
 */
export interface DisplaySession {
  readonly userId: string;
  readonly email: string;
  readonly audience: Audience;
}

interface JwtPayload {
  readonly sub?: string;
  readonly email?: string;
  readonly exp?: number;
}

/**
 * Reads the audience access cookies via `next/headers` and returns the
 * first live session found, maker before customer — a user is exactly
 * one of the two roles (`User.MatchesAudience` binds maker accounts to
 * the maker host), so at most one non-admin cookie is ever meaningful.
 * Admin sessions are excluded on purpose: the admin area has its own
 * chrome and login (`/admin/login`).
 *
 * Server-only (Server Components / layouts). Returns `null` outside a
 * request scope, with no cookie, or when the token is expired/garbled —
 * callers render the logged-out state and the middleware/API layer stays
 * authoritative.
 */
export async function getDisplaySession(): Promise<DisplaySession | null> {
  let store;
  try {
    const { cookies } = await import('next/headers');
    store = await cookies();
  } catch {
    return null;
  }

  const audiences: readonly Audience[] = ['maker', 'customer'];
  for (const audience of audiences) {
    const cookie = store.get(accessCookieName(audience));
    if (!cookie?.value) continue;

    const payload = decodeJwtPayload(cookie.value);
    if (!payload || typeof payload.sub !== 'string' || typeof payload.email !== 'string') continue;
    // An expired access token usually still has a live refresh sibling —
    // apiFetch's 401 → refresh path recovers the API session, but for
    // pure display we treat it as logged-out only when clearly stale.
    if (typeof payload.exp === 'number' && payload.exp * 1000 < Date.now()) continue;

    return { userId: payload.sub, email: payload.email, audience };
  }

  return null;
}

function decodeJwtPayload(token: string): JwtPayload | null {
  const parts = token.split('.');
  if (parts.length !== 3) return null;
  try {
    const json = Buffer.from(parts[1], 'base64url').toString('utf8');
    return JSON.parse(json) as JwtPayload;
  } catch {
    return null;
  }
}
