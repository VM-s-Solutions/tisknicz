'use client';

import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { logout } from '@/lib/api-client-helpers/auth';
import { t } from '@/lib/i18n';
import type { MessageKey } from '@/lib/i18n';

/**
 * Admin shell navigation (T-0118a, AC-3). The only client island in the
 * shell — it owns the active-section highlight (`usePathname` +
 * `aria-current`), the mobile menu toggle, and the logout action (the
 * customer profile-client logout precedent: POST /auth/logout then route
 * to /admin/login). The session gate itself lives server-side in
 * `layout.tsx`; this component renders only for an authenticated admin.
 *
 * The live sections (Přehled / Objednávky / Faktury / Audit log +, since
 * T-0118c, Výplaty / Fronta událostí / Uživatelé / Nastavení zemí) are
 * real `<Link>`s. Only sections with no slice yet (Makeři) render as
 * visibly-pending non-links (Option H: no live link to a not-yet-built
 * route).
 */

interface NavItem {
  readonly href: string;
  readonly labelKey: MessageKey;
}

const LIVE_NAV: readonly NavItem[] = [
  { href: '/dashboard/admin', labelKey: 'dashboard.admin.nav.overview' },
  { href: '/dashboard/admin/orders', labelKey: 'dashboard.admin.nav.orders' },
  { href: '/dashboard/admin/faktury', labelKey: 'dashboard.admin.nav.invoices' },
  { href: '/dashboard/admin/vyplaty', labelKey: 'dashboard.admin.nav.payouts' },
  { href: '/dashboard/admin/outbox', labelKey: 'dashboard.admin.nav.outbox' },
  { href: '/dashboard/admin/users', labelKey: 'dashboard.admin.nav.users' },
  { href: '/dashboard/admin/countries/CZ', labelKey: 'dashboard.admin.nav.config' },
  { href: '/dashboard/admin/audit', labelKey: 'dashboard.admin.nav.audit' },
];

/** Sections with no slice yet — rendered visibly pending, never as live links (AC-3). */
const PENDING_NAV: readonly MessageKey[] = ['dashboard.admin.nav.makers'];

function isActive(pathname: string, href: string): boolean {
  if (href === '/dashboard/admin') return pathname === href;
  return pathname === href || pathname.startsWith(`${href}/`);
}

export function AdminShellNav({ identity }: { readonly identity: string }) {
  const pathname = usePathname();
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [loggingOut, setLoggingOut] = useState(false);

  async function handleLogout(): Promise<void> {
    setLoggingOut(true);
    await logout('admin');
    router.push('/admin/login');
  }

  return (
    <header className="sticky top-0 z-20 border-b border-zinc-800 bg-surface-primary/95 backdrop-blur">
      <div className="mx-auto flex max-w-7xl items-center justify-between gap-4 px-4 py-3 sm:px-6 lg:px-8">
        <div className="flex items-center gap-3">
          <button
            type="button"
            aria-label={t('dashboard.admin.shell.openMenu')}
            aria-expanded={open}
            onClick={() => setOpen((v) => !v)}
            className="inline-flex h-10 w-10 items-center justify-center rounded-xl border border-zinc-700 text-zinc-300 transition-colors hover:bg-zinc-800 md:hidden"
          >
            <Icon name="chevronDown" size={18} />
          </button>
          <Link href="/dashboard/admin" className="text-base font-semibold tracking-tight text-white">
            {t('dashboard.admin.shell.brand')}
          </Link>
        </div>

        <nav className="hidden items-center gap-1 md:flex" aria-label={t('dashboard.admin.shell.brand')}>
          {LIVE_NAV.map((item) => (
            <NavLink key={item.href} item={item} active={isActive(pathname, item.href)} />
          ))}
        </nav>

        <div className="flex items-center gap-3">
          <span className="hidden max-w-[12rem] truncate text-sm text-zinc-400 sm:block">
            {identity}
          </span>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            loading={loggingOut}
            onClick={handleLogout}
          >
            {t('dashboard.admin.shell.logout')}
          </Button>
        </div>
      </div>

      {open ? (
        <nav className="border-t border-zinc-800 px-4 py-3 md:hidden" aria-label={t('dashboard.admin.shell.brand')}>
          <ul className="flex flex-col gap-1">
            {LIVE_NAV.map((item) => (
              <li key={item.href}>
                <NavLink
                  item={item}
                  active={isActive(pathname, item.href)}
                  onNavigate={() => setOpen(false)}
                  block
                />
              </li>
            ))}
            {PENDING_NAV.map((labelKey) => (
              <li key={labelKey}>
                <PendingNavEntry labelKey={labelKey} block />
              </li>
            ))}
          </ul>
        </nav>
      ) : null}
    </header>
  );
}

function NavLink({
  item,
  active,
  onNavigate,
  block = false,
}: {
  readonly item: NavItem;
  readonly active: boolean;
  readonly onNavigate?: () => void;
  readonly block?: boolean;
}) {
  return (
    <Link
      href={item.href}
      aria-current={active ? 'page' : undefined}
      onClick={onNavigate}
      className={`rounded-xl px-3 py-2 text-sm font-medium transition-colors ${
        block ? 'block' : ''
      } ${
        active
          ? 'bg-brand-400/10 text-brand-400'
          : 'text-zinc-400 hover:bg-zinc-800 hover:text-zinc-200'
      }`}
    >
      {t(item.labelKey)}
    </Link>
  );
}

function PendingNavEntry({ labelKey, block = false }: { readonly labelKey: MessageKey; readonly block?: boolean }) {
  return (
    <span
      aria-disabled="true"
      className={`flex items-center justify-between gap-2 rounded-xl px-3 py-2 text-sm font-medium text-zinc-600 ${
        block ? '' : 'inline-flex'
      }`}
    >
      {t(labelKey)}
      <span className="rounded-full bg-zinc-800 px-2 py-0.5 text-xs font-semibold uppercase tracking-wide text-zinc-500">
        {t('dashboard.admin.nav.pendingBadge')}
      </span>
    </span>
  );
}
