import type { ReactNode } from 'react';
import { DashboardNav, type DashboardNavItem } from '@/components/shared/dashboard-nav';
import { PublicNavbar } from '@/components/shared/public-navbar';
import { getDisplaySession } from '@/lib/auth/display-session';

/**
 * Chrome for /dashboard/maker/* — the authenticated maker area. Surfaces
 * every maker capability that already ships (orders incl. accept/ship
 * actions, product CRUD, payouts, review replies, ARES profile) as one
 * persistent section navigation. The edge middleware gates these routes
 * on the maker access cookie; the display session here only feeds the
 * navbar account menu.
 */
const MAKER_NAV_ITEMS: readonly DashboardNavItem[] = [
  { href: '/dashboard/maker/objednavky', labelKey: 'nav.maker.orders' },
  { href: '/dashboard/maker/produkty', labelKey: 'nav.maker.products' },
  { href: '/dashboard/maker/vyplaty', labelKey: 'nav.maker.payouts' },
  { href: '/dashboard/maker/recenze', labelKey: 'nav.maker.reviews' },
  { href: '/dashboard/maker/profil', labelKey: 'nav.maker.profile' },
];

export default async function MakerDashboardLayout({ children }: { children: ReactNode }) {
  const session = await getDisplaySession();
  return (
    <div className="min-h-screen bg-surface-primary text-zinc-100">
      <PublicNavbar session={session} />
      <DashboardNav items={MAKER_NAV_ITEMS} />
      <main>{children}</main>
    </div>
  );
}
