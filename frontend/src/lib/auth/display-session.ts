import { decodeJwtPayload } from './jwt-expiry';
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
    // The middleware refreshes an expired access cookie BEFORE the render
    // (T-0154), so an expired token reaching this point means the refresh
    // was rejected/unavailable — render logged-out.
    if (typeof payload.exp === 'number' && payload.exp * 1000 < Date.now()) continue;

    return { userId: payload.sub, email: payload.email, audience };
  }

  return null;
}

/**
 * Same decode, for the admin audience only (T-0186). Kept as its own
 * function rather than a parameter on {@link getDisplaySession}: that one
 * answers "who is browsing the public site", and it deliberately never
 * returns an admin — an admin cookie must not light up the customer
 * account menu. The admin shell asks a different question and gets a
 * different answer.
 *
 * Display-only, unverified, exactly as above: the backend validates the
 * signature and audience on every call, and the layout's own cookie gate
 * plus the edge middleware decide access. `null` when there is no cookie,
 * when it is expired, or when the payload is not a well-formed session —
 * the shell then falls back to a generic operator label rather than
 * rendering an empty slot.
 */
export async function getAdminDisplaySession(): Promise<DisplaySession | null> {
  let store;
  try {
    const { cookies } = await import('next/headers');
    store = await cookies();
  } catch {
    return null;
  }

  const cookie = store.get(accessCookieName('admin'));
  if (!cookie?.value) return null;

  const payload = decodeJwtPayload(cookie.value);
  if (!payload || typeof payload.sub !== 'string' || typeof payload.email !== 'string') return null;
  if (typeof payload.exp === 'number' && payload.exp * 1000 < Date.now()) return null;

  return { userId: payload.sub, email: payload.email, audience: 'admin' };
}
