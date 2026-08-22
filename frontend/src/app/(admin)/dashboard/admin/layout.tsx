import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import type { ReactNode } from 'react';
import { PublicFooter } from '@/components/shared/public-footer';
import { accessCookieName } from '@/lib/auth';
import { getAdminDisplaySession } from '@/lib/auth/display-session';
import { t } from '@/lib/i18n';
import { AdminShellNav } from '../../shell-nav';

/**
 * Authenticated admin shell + server-side session gate (T-0118a, AC-1/AC-3).
 *
 * Wraps every `/dashboard/admin/*` route. Reads the admin-audience access
 * cookie server-side; no cookie → redirect to the dedicated `/admin/login`
 * (NOT the customer `/login` — ADR 0013 per-host audience) with a
 * path-only `redirect` target. This complements the edge `middleware.ts`
 * admin branch (defense in depth — the middleware is presence-only until
 * T-0027 validates signatures). The `/admin/login` route is a sibling
 * OUTSIDE this layout, so an unauthenticated admin reaches it without a
 * loop.
 *
 * Phase-1 session contract (lib/auth/session.ts): the cookie presence is
 * the gate; real JWT signature validation stays with the backend on every
 * API call. T-0186 replaces the generic operator label with the admin's
 * actual sign-in, decoded (unverified, display-only) from the same cookie
 * this layout already reads — an operator with several admin accounts
 * could not previously tell which one they were acting as. A cookie that
 * passes the presence gate but carries no decodable identity still falls
 * back to the generic label rather than rendering an empty header.
 */
export default async function AdminDashboardLayout({ children }: { children: ReactNode }) {
  const store = await cookies();
  const session = store.get(accessCookieName('admin'));
  if (!session?.value) {
    redirect('/admin/login?redirect=%2Fdashboard%2Fadmin');
  }

  const identity = await getAdminDisplaySession();

  return (
    <div className="relative min-h-screen text-zinc-100">
      <AdminShellNav identity={identity?.email ?? t('dashboard.admin.shell.identityFallback')} />
      <main>{children}</main>
      <PublicFooter />
    </div>
  );
}
