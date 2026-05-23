import { NextResponse, type NextRequest } from 'next/server';
import { accessCookieName, type Audience } from '@/lib/auth';

/**
 * Edge middleware that protects authenticated route groups.
 *
 * Phase 1 ships the wiring only — it checks for the presence of the
 * audience-scoped access cookie and redirects unauthenticated requests
 * to `/auth/login`. Full JWT validation (signature, audience, expiry)
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
    loginUrl.pathname = '/auth/login';
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
  matcher: [
    '/dashboard/zakaznik/:path*',
    '/dashboard/maker/:path*',
    '/dashboard/admin/:path*',
  ],
};
