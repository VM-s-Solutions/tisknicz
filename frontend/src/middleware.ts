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
 * Per-audience refresh budget. This fetch BLOCKS the page render, and up to
 * three of them can be needed at once, so the ceiling has to stay well under
 * anything a human would call a hang. 8 s was the original value and meant a
 * single unreachable host could stall a render for the whole of it.
 */
const REFRESH_TIMEOUT_MS = 3000;

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

/**
 * Outcome of one refresh attempt. The distinction matters: a token the
 * backend has DEFINITIVELY rejected is dead forever, so its cookies must be
 * dropped — otherwise every subsequent request re-attempts the same doomed
 * refresh and pays a blocking backend round trip for it, on every page and
 * every RSC prefetch, until the visitor manually clears cookies (T-0189,
 * reported as "the app is slow in Safari but fine in Chrome" — cookies are
 * per-browser, so only the browser holding the stale ones paid the tax).
 *
 * A backend that is merely unreachable (blip, cold start, timeout) must NOT
 * clear anything — that would log people out on a hiccup.
 */
type RefreshOutcome =
  | { readonly status: 'rotated'; readonly setCookies: readonly string[] }
  | { readonly status: 'rejected' }
  | { readonly status: 'unavailable' };

const REJECTED: RefreshOutcome = { status: 'rejected' };
const UNAVAILABLE: RefreshOutcome = { status: 'unavailable' };

/** In-flight refresh de-dupe: refresh-token value → pending outcome. */
const inflightRefreshes = new Map<string, Promise<RefreshOutcome>>();

function refreshSession(audience: Audience, refreshToken: string): Promise<RefreshOutcome> {
  const existing = inflightRefreshes.get(refreshToken);
  if (existing) return existing;

  const attempt = (async (): Promise<RefreshOutcome> => {
    const origin = refreshOrigin(audience);
    if (!origin) return UNAVAILABLE;
    try {
      const response = await fetch(`${origin}/api/v1/auth/refresh`, {
        method: 'POST',
        headers: { Cookie: `${refreshCookieName(audience)}=${refreshToken}` },
        // The backend reads the refresh token from the cookie; no body.
        signal: AbortSignal.timeout(REFRESH_TIMEOUT_MS),
      });
      if (response.ok) {
        const setCookies = response.headers.getSetCookie();
        // A 200 with no Set-Cookie is not a usable rotation, but it is also
        // not the backend calling the token dead — treat it as a blip.
        return setCookies.length > 0 ? { status: 'rotated', setCookies } : UNAVAILABLE;
      }
      // 401/403 = this refresh token is spent, revoked, reused, or signed by
      // a retired key. Nothing will ever make it work again.
      if (response.status === 401 || response.status === 403) return REJECTED;
      // 5xx / 429 / anything else — the token may still be good.
      return UNAVAILABLE;
    } catch {
      // Backend unreachable or timed out — leave cookies untouched; the next
      // request retries. Deleting here would log the user out on a blip.
      return UNAVAILABLE;
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
  const deadCookieNames: string[] = [];

  // Which audiences actually need a refresh on this request.
  const stale = AUDIENCES.filter((audience) => {
    const refreshValue = request.cookies.get(refreshCookieName(audience))?.value;
    if (!refreshValue) return false;
    const accessValue = request.cookies.get(accessCookieName(audience))?.value;
    return !(accessValue && !isJwtExpiredOrInvalid(accessValue));
  });

  // Concurrently, not in sequence. These are independent per-host calls that
  // BLOCK the render; awaiting them one after another made a browser holding
  // stale cookies for all three audiences pay three round trips back to back
  // on every single page and RSC request (CLAUDE.md §5).
  const outcomes = await Promise.all(
    stale.map(async (audience) => {
      const refreshValue = request.cookies.get(refreshCookieName(audience))!.value;
      return [audience, await refreshSession(audience, refreshValue)] as const;
    }),
  );

  for (const [audience, outcome] of outcomes) {
    if (outcome.status === 'unavailable') continue;

    if (outcome.status === 'rejected') {
      // Drop the dead pair so the next request skips the refresh entirely
      // instead of re-paying for a token that can never succeed. The visitor
      // is logged out either way — this only decides whether they also get a
      // permanent latency tax on every navigation until they clear cookies.
      deadCookieNames.push(accessCookieName(audience), refreshCookieName(audience));
      continue;
    }

    for (const sc of outcome.setCookies) {
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
  if (patchedPairs.size > 0 || deadCookieNames.length > 0) {
    const requestHeaders = new Headers(request.headers);
    const existing = requestHeaders.get('cookie') ?? '';
    const dropped = new Set(deadCookieNames);
    const kept = existing
      .split(';')
      .map((part) => part.trim())
      .filter((part) => {
        if (part === '') return false;
        const name = part.split('=', 1)[0] ?? '';
        // Rejected cookies must not reach this render either — the guard and
        // the display session would otherwise still read a dead session.
        return !patchedPairs.has(name) && !dropped.has(name);
      });
    requestHeaders.set('cookie', [...kept, ...patchedPairs.values()].join('; '));
    response = guardOrNext(request, requestHeaders, patchedPairs, dropped);
  } else {
    response = guardOrNext(request, null, patchedPairs);
  }

  for (const sc of forwardedSetCookies) {
    response.headers.append('set-cookie', sc);
  }
  // Expire the rejected pairs in the browser. Path=/ matches how the backend
  // issues them; without a matching Path the browser keeps the original and
  // the tax survives.
  for (const name of deadCookieNames) {
    response.headers.append(
      'set-cookie',
      `${name}=; Path=/; Max-Age=0; HttpOnly; SameSite=Strict${request.nextUrl.protocol === 'https:' ? '; Secure' : ''}`,
    );
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
  deadCookieNames: ReadonlySet<string> = new Set(),
): NextResponse {
  const audience = guardedRouteAudience(request.nextUrl.pathname);
  if (audience) {
    const cookieName = accessCookieName(audience);
    // A cookie whose refresh the backend just rejected is being expired on
    // this very response, so it must not count as access here either — the
    // guard would otherwise wave the visitor into a dashboard where every
    // call 401s, which reads as a broken page rather than a logged-out one.
    const hasAccess =
      patchedPairs.has(cookieName) ||
      (!deadCookieNames.has(cookieName) && Boolean(request.cookies.get(cookieName)?.value));
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
