import { NextRequest } from 'next/server';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * T-0189 — "problem byly stare cookies".
 *
 * The middleware matcher covers every page and RSC request. For each audience
 * whose access cookie is dead but whose refresh cookie is present it makes a
 * BLOCKING server-side call to that host's /auth/refresh. Before this fix a
 * rejected refresh was indistinguishable from an unreachable backend, so the
 * dead cookies were never cleared and every subsequent navigation re-paid the
 * same doomed round trip — forever, until the visitor cleared cookies by hand.
 * Cookies are per-browser, which is why it presented as "slow in Safari, fine
 * in Chrome".
 */

const ORIGIN = 'https://api.example.test';
// An access JWT that is structurally valid but long expired, so the middleware
// treats it as needing refresh rather than as a live session.
const EXPIRED_JWT = [
  btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' })),
  btoa(JSON.stringify({ exp: 1000, sub: 'u1' })),
  'sig',
].join('.');

let middleware: (req: NextRequest) => Promise<Response>;

function request(pathname: string, cookies: Record<string, string>): NextRequest {
  const req = new NextRequest(new URL(`https://app.example.test${pathname}`));
  for (const [name, value] of Object.entries(cookies)) req.cookies.set(name, value);
  return req;
}

function setCookieNames(res: Response): string[] {
  return res.headers.getSetCookie().map((sc) => sc.split('=', 1)[0] ?? '');
}

function expiredCookieNames(res: Response): string[] {
  return res.headers
    .getSetCookie()
    .filter((sc) => /Max-Age=0/i.test(sc))
    .map((sc) => sc.split('=', 1)[0] ?? '');
}

beforeEach(async () => {
  vi.resetModules();
  process.env.API_CUSTOMER_INTERNAL_BASE_URL = ORIGIN;
  process.env.API_MAKER_INTERNAL_BASE_URL = ORIGIN;
  process.env.API_ADMIN_INTERNAL_BASE_URL = ORIGIN;
  ({ middleware } = await import('../middleware'));
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('middleware: a refresh the backend rejects', () => {
  it('expires the dead pair so the next request stops paying for it', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 401 })));

    const res = await middleware(
      request('/katalog', {
        makables_access_customer: EXPIRED_JWT,
        makables_refresh_customer: 'dead-token',
      }),
    );

    expect(expiredCookieNames(res).sort()).toEqual([
      'makables_access_customer',
      'makables_refresh_customer',
    ]);
  });

  it('leaves the cookies alone when the backend is merely unreachable', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new Error('ECONNREFUSED'); }));

    const res = await middleware(
      request('/katalog', {
        makables_access_customer: EXPIRED_JWT,
        makables_refresh_customer: 'maybe-good-token',
      }),
    );

    // A blip must never log anyone out.
    expect(expiredCookieNames(res)).toEqual([]);
  });

  it('treats a 5xx as unavailable, not as a dead token', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 503 })));

    const res = await middleware(
      request('/katalog', {
        makables_access_customer: EXPIRED_JWT,
        makables_refresh_customer: 'maybe-good-token',
      }),
    );

    expect(expiredCookieNames(res)).toEqual([]);
  });

  it('forwards the rotated cookies when the refresh succeeds', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => {
      const h = new Headers();
      h.append('set-cookie', 'makables_access_customer=fresh; Path=/');
      h.append('set-cookie', 'makables_refresh_customer=fresh-r; Path=/');
      return new Response(null, { status: 200, headers: h });
    }));

    const res = await middleware(
      request('/katalog', {
        makables_access_customer: EXPIRED_JWT,
        makables_refresh_customer: 'good-token',
      }),
    );

    expect(setCookieNames(res).sort()).toEqual([
      'makables_access_customer',
      'makables_refresh_customer',
    ]);
    expect(expiredCookieNames(res)).toEqual([]);
  });
});

describe('middleware: several stale audiences', () => {
  it('refreshes them concurrently rather than one blocking call after another', async () => {
    let inFlight = 0;
    let peak = 0;
    vi.stubGlobal('fetch', vi.fn(async () => {
      inFlight += 1;
      peak = Math.max(peak, inFlight);
      await new Promise((r) => setTimeout(r, 20));
      inFlight -= 1;
      return new Response(null, { status: 401 });
    }));

    await middleware(
      request('/katalog', {
        makables_access_customer: EXPIRED_JWT,
        makables_refresh_customer: 'dead-c',
        makables_access_maker: EXPIRED_JWT,
        makables_refresh_maker: 'dead-m',
        makables_access_admin: EXPIRED_JWT,
        makables_refresh_admin: 'dead-a',
      }),
    );

    // Sequential awaits would never exceed one concurrent request.
    expect(peak).toBe(3);
  });

  it('makes no backend call at all once the dead cookies are gone', async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 401 }));
    vi.stubGlobal('fetch', fetchMock);

    await middleware(request('/katalog', {}));

    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe('middleware: guard', () => {
  it('does not wave a visitor into the dashboard on a cookie it just killed', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 401 })));

    const res = await middleware(
      request('/dashboard/zakaznik/objednavky', {
        makables_access_customer: EXPIRED_JWT,
        makables_refresh_customer: 'dead-token',
      }),
    );

    expect(res.status).toBe(307);
    expect(res.headers.get('location')).toContain('/login');
  });
});
