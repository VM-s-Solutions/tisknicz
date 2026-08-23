'use client';

import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { DashboardNav, type DashboardNavItem } from '@/components/shared/dashboard-nav';
import { ThemeToggle } from '@/components/shared/theme-toggle';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { logout } from '@/lib/api-client-helpers/auth';
import { t } from '@/lib/i18n';

/**
 * Admin shell chrome (T-0118a AC-3, redesigned in T-0186).
 *
 * The header is TWO rows, and that is the whole point of the redesign.
 * One row could not hold a brand, ten section links and an account block
 * side by side: `justify-between` handed the middle nav as much width as
 * it wanted, the brand lost its own and broke across two lines, the nav
 * wrapped into ragged rows of different lengths, and the identity was
 * squeezed into a `max-w-48 truncate` that cut the operator's own name in
 * half. Splitting identity (row 1) from navigation (row 2) removes the
 * competition instead of tuning it:
 *
 * <list type="bullet">
 *   <item><description>Row 1 — brand + who you are + sign out. Nothing here can wrap: the brand is `whitespace-nowrap`, and with the nav gone from this row the identity has room to render in full.</description></item>
 *   <item><description>Row 2 — the section rail, delegated to the shared <see cref="DashboardNav"/> that the customer and maker dashboards already use. It scrolls horizontally instead of wrapping, so ten sections stay on one honest line at every width and the admin console finally looks like the rest of the app.</description></item>
 * </list>
 *
 * Sharing the rail also retires this file's private nav-link renderer and
 * its own mobile drawer — one section-navigation implementation for all
 * three audiences. The `exact` flag exists for the overview: its href is a
 * prefix of every other admin route, so a prefix match would light it up
 * on every page.
 *
 * Still the only client island in the shell — the session gate is
 * server-side in `layout.tsx`; this renders only for an authenticated
 * admin.
 */
const ADMIN_NAV: readonly DashboardNavItem[] = [
  { href: '/dashboard/admin', labelKey: 'dashboard.admin.nav.overview', icon: 'barChart', exact: true },
  { href: '/dashboard/admin/orders', labelKey: 'dashboard.admin.nav.orders', icon: 'package' },
  { href: '/dashboard/admin/faktury', labelKey: 'dashboard.admin.nav.invoices', icon: 'receipt' },
  { href: '/dashboard/admin/vyplaty', labelKey: 'dashboard.admin.nav.payouts', icon: 'wallet' },
  { href: '/dashboard/admin/outbox', labelKey: 'dashboard.admin.nav.outbox', icon: 'refresh' },
  { href: '/dashboard/admin/users', labelKey: 'dashboard.admin.nav.users', icon: 'users' },
  { href: '/dashboard/admin/countries/CZ', labelKey: 'dashboard.admin.nav.config', icon: 'globe' },
  { href: '/dashboard/admin/kategorie', labelKey: 'dashboard.admin.nav.categories', icon: 'tag' },
  { href: '/dashboard/admin/makers', labelKey: 'dashboard.admin.nav.makers', icon: 'building' },
  { href: '/dashboard/admin/audit', labelKey: 'dashboard.admin.nav.audit', icon: 'shield' },
];

export function AdminShellNav({ identity }: { readonly identity: string }) {
  const router = useRouter();
  const [loggingOut, setLoggingOut] = useState(false);
  const [logoutError, setLogoutError] = useState<string | null>(null);

  async function handleLogout(): Promise<void> {
    if (loggingOut) return;
    setLoggingOut(true);
    setLogoutError(null);
    const result = await logout('admin');
    setLoggingOut(false);
    if (result.success) {
      router.push('/admin/login');
      // Re-render the server tree so the shell picks up the cleared cookie.
      router.refresh();
      return;
    }
    // A failed logout used to be silent here — the button stopped spinning
    // and nothing else happened, leaving the operator unsure whether the
    // admin session was actually closed (public-navbar precedent, T-0171).
    setLogoutError(t('dashboard.admin.shell.logoutFailed'));
  }

  return (
    // No bottom border on the header itself — the nav rail is the last row
    // and carries it, so keeping both would stack two hairlines into one
    // thick seam.
    <header className="sticky top-0 z-20 bg-surface-primary">
      <div className="mx-auto flex max-w-7xl items-center justify-between gap-4 px-4 py-3 sm:px-6 lg:px-8">
        <Link
          href="/dashboard/admin"
          className="flex shrink-0 items-center gap-2 whitespace-nowrap rounded-lg text-base font-semibold tracking-tight text-zinc-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/60"
        >
          {t('dashboard.admin.shell.brandName')}
          {/* The console badge, not part of the wordmark — it keeps
              "Makables" and "Admin" from ever being wrapped apart. */}
          <span className="rounded-md border border-brand-500/40 px-1.5 py-0.5 text-[11px] font-semibold uppercase tracking-widest text-brand-300">
            {t('dashboard.admin.shell.brandBadge')}
          </span>
        </Link>

        <div className="flex min-w-0 items-center gap-2 sm:gap-3">
          <ThemeToggle />
          {/* Full identity, no truncation: row 1 no longer shares its width
              with the section links, so the operator can read their whole
              sign-in. `break-all` keeps even a long address complete rather
              than clipping it. */}
          <span className="hidden min-w-0 items-center gap-2 rounded-lg border border-zinc-800 bg-surface-secondary/60 px-3 py-1.5 text-sm text-zinc-300 sm:flex">
            <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-md bg-brand-500/15 text-brand-300">
              <Icon name="user" size={13} strokeWidth={1.75} />
            </span>
            <span className="break-all">{identity}</span>
          </span>

          {/* Signing out of an admin console is routine and reversible, so
              it reads as a neutral hairline action. The red-bordered
              destructive weight belongs on refunds and erasures — spending
              it here made the loudest control on the page the one that does
              the least. */}
          <Button
            type="button"
            variant="outline"
            size="sm"
            loading={loggingOut}
            onClick={handleLogout}
          >
            <Icon name="logOut" size={15} />
            {loggingOut ? t('dashboard.admin.shell.loggingOut') : t('dashboard.admin.shell.logout')}
          </Button>
        </div>
      </div>

      {logoutError ? (
        <p
          role="alert"
          className="mx-auto max-w-7xl px-4 pb-2 text-right text-xs text-error sm:px-6 lg:px-8"
        >
          {logoutError}
        </p>
      ) : null}

      <DashboardNav items={ADMIN_NAV} ariaLabelKey="dashboard.admin.shell.navAria" />
    </header>
  );
}
