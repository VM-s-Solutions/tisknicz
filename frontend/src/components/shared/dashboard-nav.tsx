'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { Icon, type IconName } from '@/components/ui/icon';
import { t, type MessageKey } from '@/lib/i18n';

export interface DashboardNavItem {
  readonly href: string;
  readonly labelKey: MessageKey;
  readonly icon: IconName;
  /**
   * Highlight this item ONLY on an exact path match. Needed for a section
   * root that is a prefix of every sibling (the admin overview lives at
   * `/dashboard/admin`, so a prefix match would light it up on every
   * admin page at once). Omitted ⇒ the item also matches its own
   * sub-routes, so a detail page keeps its section highlighted.
   */
  readonly exact?: boolean;
}

/**
 * Horizontal section navigation for the authenticated dashboard areas
 * (customer + maker), styled as an iconed pill rail. Server layouts own
 * the item list per audience; this client boundary exists only for the
 * `usePathname` active state. Scrolls horizontally on narrow viewports
 * instead of wrapping.
 */
export function DashboardNav({
  items,
  ariaLabelKey = 'nav.dashboard_aria',
}: {
  items: readonly DashboardNavItem[];
  /**
   * Landmark label for the rail. Defaults to the account-navigation
   * wording the customer/maker dashboards use; the admin console passes
   * its own so a screen reader announces a console section rail rather
   * than a personal account menu.
   */
  ariaLabelKey?: MessageKey;
}) {
  const pathname = usePathname();

  return (
    <nav
      className="border-b border-zinc-800/80 bg-surface-secondary/40"
      aria-label={t(ariaLabelKey)}
    >
      <div className="mx-auto w-full max-w-7xl overflow-x-auto px-4 sm:px-6 lg:px-8">
        {/* gap-1 + px-3 rather than the roomier gap-2/px-4 this rail shipped
            with: the admin console has ten sections and the roomier spacing
            pushed the last one ("Audit log") past the 1280 content width, so
            the primary desktop width could not reach it without scrolling.
            The five-item customer/maker rails are unaffected — they had room
            to spare either way. */}
        <div className="flex items-center gap-1 py-3">
          {items.map((item) => {
            const active = item.exact
              ? pathname === item.href
              : pathname === item.href || pathname.startsWith(`${item.href}/`);
            return (
              <Link
                key={item.href}
                href={item.href}
                className={`inline-flex items-center gap-1.5 rounded-lg border px-3 py-2 text-sm font-medium whitespace-nowrap transition-colors ${
                  active
                    ? 'border-brand-500/40 bg-tint-brand-strong text-on-tint-brand'
                    : 'border-transparent text-zinc-400 hover:border-zinc-700 hover:bg-zinc-800/50 hover:text-zinc-100'
                }`}
                aria-current={active ? 'page' : undefined}
              >
                <Icon name={item.icon} size={16} className={active ? 'text-brand-300' : 'text-zinc-500'} />
                {t(item.labelKey)}
              </Link>
            );
          })}
        </div>
      </div>
    </nav>
  );
}
