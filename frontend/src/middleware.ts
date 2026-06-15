import { NextResponse, type NextRequest } from 'next/server';
import { accessCookieName, type Audience } from '@/lib/auth';

/**
 * Edge middleware that protects authenticated route groups.
 *
 * Phase 1 ships the wiring only — it checks for the presence of the
 * audience-scoped access cookie and redirects unauthenticated requests
 * to `/login`. Full JWT validation (signature, audience, expiry)
 * lands in T-0027 when the IJwtIssuer ships real signing keys.
 *
 * Routes are matched by the `(customer)`, `(maker)`, `(admin)` route
 * groups under `app/`. Public surfaces and `/auth/*` are unaffected.
 */
export function middleware(request: NextRequest): NextResponse {
  const audience = inferAudience(request.nextUrl.pathname);
  if (!audience) {
    return NextResponse.next();
  }

  const cookie = request.cookies.get(accessCookieName(audience));
  if (!cookie?.value) {
    const loginUrl = request.nextUrl.clone();
    // Admins log in on the dedicated /admin/login (ADR 0013 per-host
    // audience — a customer token is useless against the admin host).
    // Customer/maker branches keep the shared /login. T-0118a retargets
    // ONLY the admin branch.
    loginUrl.pathname = audience === 'admin' ? '/admin/login' : '/login';
    loginUrl.searchParams.set('redirect', request.nextUrl.pathname);
    return NextResponse.redirect(loginUrl);
  }

  return NextResponse.next();
}

function inferAudience(pathname: string): Audience | undefined {
  if (pathname.startsWith('/dashboard/zakaznik')) return 'customer';
  if (pathname.startsWith('/dashboard/maker')) return 'maker';
  if (pathname.startsWith('/dashboard/admin')) return 'admin';
  return undefined;
}

export const config = {
  // /objednavka/* is part of the (customer) route group per ADR 0005 but
  // the matcher omits it for Phase 1: the post-payment confirmation
  // (/objednavka/potvrzeni) needs to be reachable unauthenticated for
  // customers returning from Comgate's redirect. T-0084 reintroduces the
  // real /objednavka/* pages and revisits middleware coverage then.
  matcher: [
    '/dashboard/zakaznik/:path*',
    '/dashboard/maker/:path*',
    '/dashboard/admin/:path*',
  ],
};
