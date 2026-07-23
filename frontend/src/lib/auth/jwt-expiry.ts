/**
 * Edge-safe JWT payload decoding — used by BOTH the edge middleware
 * (session refresh, T-0154) and the Node-side display session. No
 * signature verification anywhere here by design: the frontend has no
 * signing key and derives no authorization from these reads — the
 * backend validates every API call; this only decides whether a refresh
 * attempt / display render is warranted.
 *
 * Uses `atob` + `TextDecoder` (available in both the edge and Node
 * runtimes) instead of `Buffer` so the middleware bundle stays
 * edge-compatible.
 */

export interface JwtDisplayPayload {
  readonly sub?: string;
  readonly email?: string;
  readonly exp?: number;
}

export function decodeJwtPayload(token: string): JwtDisplayPayload | null {
  const parts = token.split('.');
  if (parts.length !== 3) return null;
  try {
    const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
    const bytes = Uint8Array.from(atob(padded), (c) => c.charCodeAt(0));
    const json = new TextDecoder().decode(bytes);
    return JSON.parse(json) as JwtDisplayPayload;
  } catch {
    return null;
  }
}

/**
 * True when the token is garbled or its `exp` has passed (with a small
 * skew allowance so a token expiring mid-request counts as expired and
 * gets refreshed proactively rather than 401-ing downstream).
 */
export function isJwtExpiredOrInvalid(token: string, skewMs = 15_000): boolean {
  const payload = decodeJwtPayload(token);
  if (!payload || typeof payload.exp !== 'number') return true;
  return payload.exp * 1000 <= Date.now() + skewMs;
}
