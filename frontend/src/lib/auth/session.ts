/**
 * Session scaffolding. T-0027 wires real JWT validation + refresh; this
 * file establishes the cookie + role shape so middleware, layouts, and
 * Server Components can be built against a stable contract.
 *
 * Per ADR 0012 (auth): access JWT lives in an `httpOnly` cookie scoped to
 * the audience that issued it; refresh token in a sibling `httpOnly`
 * cookie. The audience is encoded into the cookie name so a customer
 * token can't accidentally be sent to the maker host.
 */

export type Audience = 'customer' | 'maker' | 'admin';

export type Role = 'customer' | 'maker' | 'admin';

export interface Session {
  readonly userId: string;
  readonly email: string;
  readonly countryCode: string;
  readonly role: Role;
  readonly audience: Audience;
  /** Expiry in epoch milliseconds. */
  readonly expiresAt: number;
}

/** Cookie name conventions (T-0027 enforces). */
export const ACCESS_COOKIE_PREFIX = 'makables_access_';
export const REFRESH_COOKIE_PREFIX = 'makables_refresh_';

export function accessCookieName(audience: Audience): string {
  return `${ACCESS_COOKIE_PREFIX}${audience}`;
}

export function refreshCookieName(audience: Audience): string {
  return `${REFRESH_COOKIE_PREFIX}${audience}`;
}
