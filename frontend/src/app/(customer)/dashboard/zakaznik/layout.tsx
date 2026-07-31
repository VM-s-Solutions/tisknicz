import type { ReactNode } from 'react';
import { DashboardNav, type DashboardNavItem } from '@/components/shared/dashboard-nav';
import { PublicFooter } from '@/components/shared/public-footer';
import { PublicNavbar } from '@/components/shared/public-navbar';
import { getDisplaySession } from '@/lib/auth/display-session';

/**
 * Chrome for /dashboard/zakaznik/* — the authenticated customer area.
 * The edge middleware already gates these routes on the customer access
 * cookie; the display session here only feeds the navbar account menu.
 */
const CUSTOMER_NAV_ITEMS: readonly DashboardNavItem[] = [
  { href: '/dashboard/zakaznik/objednavky', labelKey: 'nav.customer.orders', icon: 'shoppingBag' },
  { href: '/dashboard/zakaznik/profile', labelKey: 'nav.customer.profile', icon: 'user' },
];

export default async function CustomerDashboardLayout({ children }: { children: ReactNode }) {
  const session = await getDisplaySession();
  return (
    <div className="relative min-h-screen text-zinc-100">
      <PublicNavbar session={session} />
      <DashboardNav items={CUSTOMER_NAV_ITEMS} />
      <main>{children}</main>
      <PublicFooter />
    </div>
  );
}
