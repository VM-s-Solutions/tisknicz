import { NextResponse, type NextRequest } from 'next/server';
import {
  accessCookieName,
  guardedRouteAudience,
  refreshCookieName,
  type Audience,
} from '@/lib/auth';
import { isJwtExpiredOrInvalid } from '@/lib/auth/jwt-expiry';

/**
 * Edge middleware: keeps the session alive + protects authenticated
 * route groups.
 *
 * SESSION REFRESH (T-0154 — "does not hold logged in state"): the access
 * JWT and its cookie live only 15 minutes (`JwtOptions.AccessTokenLifetime`)
 * while the refresh cookie lives for the rotated-family lifetime — and
 * until this middleware nothing on the frontend ever called
 * `/api/v1/auth/refresh`, so every session silently evaporated within
 * 15 minutes. Now: on any page/RSC request where an audience's access
 * cookie is missing/expired but its refresh cookie is present, the
 * middleware calls the audience host's refresh endpoint server-side
 * (cookie-in / Set-Cookie-out, rate-limit-exempt by design — the
 * backend comment even says "the frontend auto-calls it on 401"). The
 * rotated cookies are forwarded to the browser AND patched into the
 * current request so this very render already sees the fresh session.
 *
 * CONCURRENCY: refresh-token reuse detection revokes the WHOLE token
 * family (ADR 0012 — stolen-token replay defense), so two parallel
 * requests racing the same refresh token would hard-log-out the user.
 * In-flight refreshes are de-duplicated per token value via a
 * module-level promise map (one Node process in the standalone deploy,
 * one map per isolate on edge — either way parallel same-token races in
 * the same runtime collapse to one backend call).
 *
 * ROUTE GUARD (pre-existing): unauthenticated dashboard requests
 * redirect to the matching login. Runs AFTER the refresh attempt so a
 * user with only a live refresh cookie passes instead of bouncing.
 */

const AUDIENCES: readonly Audience[] = ['customer', 'maker', 'admin'];

/**
 * Server-side origin per audience host. Mirrors the resolution in
 * `lib/runtime/api-fetch.ts`: internal absolute origin first (deployed —
 * also feeds the /api-proxy rewrites), then the public base when it is
 * absolute (localhost dev), never a proxy-relative path (middleware has
 * no page origin to resolve it against).
 */
function refreshOrigin(audience: Audience): string | null {
  const internal = {
    customer: process.env.API_CUSTOMER_INTERNAL_BASE_URL,
    maker: process.env.API_MAKER_INTERNAL_BASE_URL,
    admin: process.env.API_ADMIN_INTERNAL_BASE_URL,
  }[audience];
  if (internal) return internal.replace(/\/+$/, '');

  const publicBase = {
    customer: process.env.NEXT_PUBLIC_API_CUSTOMER_BASE_URL ?? 'http://localhost:5001',
    maker: process.env.NEXT_PUBLIC_API_MAKER_BASE_URL ?? 'http://localhost:5002',
    admin: process.env.NEXT_PUBLIC_API_ADMIN_BASE_URL ?? 'http://localhost:5003',
  }[audience];
  return publicBase.startsWith('/') ? null : publicBase.replace(/\/+$/, '');
}

/** In-flight refresh de-dupe: refresh-token value → pending Set-Cookie strings (null = refresh rejected). */
const inflightRefreshes = new Map<string, Promise<readonly string[] | null>>();

function refreshSession(audience: Audience, refreshToken: string): Promise<readonly string[] | null> {
  const existing = inflightRefreshes.get(refreshToken);
  if (existing) return existing;

  const attempt = (async (): Promise<readonly string[] | null> => {
    const origin = refreshOrigin(audience);
    if (!origin) return null;
    try {
      const response = await fetch(`${origin}/api/v1/auth/refresh`, {
        method: 'POST',
        headers: { Cookie: `${refreshCookieName(audience)}=${refreshToken}` },
        // The backend reads the refresh token from the cookie; no body.
        signal: AbortSignal.timeout(8000),
      });
      if (!response.ok) return null;
      const setCookies = response.headers.getSetCookie();
      return setCookies.length > 0 ? setCookies : null;
    } catch {
      // Backend unreachable — leave cookies untouched; the next request
      // retries. Deleting here would log the user out on a blip.
      return null;
    }
  })();

  inflightRefreshes.set(refreshToken, attempt);
  void attempt.finally(() => inflightRefreshes.delete(refreshToken));
  return attempt;
}

/** First `name=value` pair of a Set-Cookie string, or null for deletions/others. */
function cookiePairFor(setCookie: string, name: string): string | null {
  const pair = setCookie.split(';', 1)[0]?.trim();
  if (!pair?.startsWith(`${name}=`)) return null;
  const value = pair.slice(name.length + 1);
  return value.length > 0 ? pair : null;
}

export async function middleware(request: NextRequest): Promise<NextResponse> {
  const forwardedSetCookies: string[] = [];
  const patchedPairs = new Map<string, string>();

  for (const audience of AUDIENCES) {
    const refreshValue = request.cookies.get(refreshCookieName(audience))?.value;
    if (!refreshValue) continue;

    const accessValue = request.cookies.get(accessCookieName(audience))?.value;
    if (accessValue && !isJwtExpiredOrInvalid(accessValue)) continue;

    const setCookies = await refreshSession(audience, refreshValue);
    if (!setCookies) continue;

    for (const sc of setCookies) {
      forwardedSetCookies.push(sc);
      for (const name of [accessCookieName(audience), refreshCookieName(audience)]) {
        const pair = cookiePairFor(sc, name);
        if (pair) patchedPairs.set(name, pair);
      }
    }
  }

  // Patch the CURRENT request's cookies so this render (Server
  // Components, display session, the guard below) already sees the
  // rotated tokens — the browser only learns them via Set-Cookie on the
  // response.
  let response: NextResponse;
  if (patchedPairs.size > 0) {
    const requestHeaders = new Headers(request.headers);
    const existing = requestHeaders.get('cookie') ?? '';
    const kept = existing
      .split(';')
      .map((part) => part.trim())
      .filter((part) => part !== '' && !patchedPairs.has(part.split('=', 1)[0] ?? ''));
    requestHeaders.set('cookie', [...kept, ...patchedPairs.values()].join('; '));
    response = guardOrNext(request, requestHeaders, patchedPairs);
  } else {
    response = guardOrNext(request, null, patchedPairs);
  }

  for (const sc of forwardedSetCookies) {
    response.headers.append('set-cookie', sc);
  }
  return response;
}

/**
 * The pre-existing dashboard guard, evaluated against the
 * possibly-just-refreshed cookie state. Admins log in on the dedicated
 * /admin/login (T-0118a); customer/maker share /login.
 */
function guardOrNext(
  request: NextRequest,
  patchedRequestHeaders: Headers | null,
  patchedPairs: Map<string, string>,
): NextResponse {
  const audience = guardedRouteAudience(request.nextUrl.pathname);
  if (audience) {
    const cookieName = accessCookieName(audience);
    const hasAccess =
      patchedPairs.has(cookieName) || Boolean(request.cookies.get(cookieName)?.value);
    if (!hasAccess) {
      const loginUrl = request.nextUrl.clone();
      loginUrl.pathname = audience === 'admin' ? '/admin/login' : '/login';
      // T-0169 (audit PUB-L7 / CUST-M6): the redirect carried only the
      // pathname, so a shared deep link like
      // /dashboard/zakaznik/objednavky?state=Shipped&page=3 came back as
      // the unfiltered page 1 after login. Search params ride along.
      loginUrl.searchParams.set('redirect', `${request.nextUrl.pathname}${request.nextUrl.search}`);
      return NextResponse.redirect(loginUrl);
    }
  }

  return patchedRequestHeaders
    ? NextResponse.next({ request: { headers: patchedRequestHeaders } })
    : NextResponse.next();
}

export const config = {
  // Session refresh must see every page + RSC request (any surface can
  // render the session-aware navbar), so the matcher covers everything
  // except Next internals and static files (paths with a dot: icons,
  // sitemap.xml, images…). The old dashboard-only matcher lives on as
  // the guard inside the handler. /objednavka/* note from Phase 1 still
  // applies: the payment-confirmation page stays reachable
  // unauthenticated; the guard only covers /dashboard/*.
  matcher: ['/((?!_next/static|_next/image|.*\\..*).*)'],
};
