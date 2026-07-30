import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import type { ReactNode } from 'react';
import { accessCookieName } from '@/lib/auth';
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
 * the gate; real JWT validation + the decoded identity land in T-0027.
 * Until then the header shows a generic operator label (the identity is
 * not yet decodable client-side without the token body).
 */
export default async function AdminDashboardLayout({ children }: { children: ReactNode }) {
  const store = await cookies();
  const session = store.get(accessCookieName('admin'));
  if (!session?.value) {
    redirect('/admin/login?redirect=%2Fdashboard%2Fadmin');
  }

  return (
    <div className="relative min-h-screen text-zinc-100">
      <div aria-hidden="true" className="page-backdrop" />
      <AdminShellNav identity={t('dashboard.admin.shell.identityFallback')} />
      <main>{children}</main>
    </div>
  );
}
