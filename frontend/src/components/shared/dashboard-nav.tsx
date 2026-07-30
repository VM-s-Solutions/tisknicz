'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { Icon, type IconName } from '@/components/ui/icon';
import { t, type MessageKey } from '@/lib/i18n';

export interface DashboardNavItem {
  readonly href: string;
  readonly labelKey: MessageKey;
  readonly icon: IconName;
}

/**
 * Horizontal section navigation for the authenticated dashboard areas
 * (customer + maker), styled as an iconed pill rail. Server layouts own
 * the item list per audience; this client boundary exists only for the
 * `usePathname` active state. Scrolls horizontally on narrow viewports
 * instead of wrapping.
 */
export function DashboardNav({ items }: { items: readonly DashboardNavItem[] }) {
  const pathname = usePathname();

  return (
    <nav
      className="border-b border-zinc-800/80 bg-surface-secondary/40"
      aria-label={t('nav.dashboard_aria')}
    >
      <div className="mx-auto w-full max-w-7xl overflow-x-auto px-4 sm:px-6 lg:px-8">
        <div className="flex items-center gap-2 py-3">
          {items.map((item) => {
            const active = pathname.startsWith(item.href);
            return (
              <Link
                key={item.href}
                href={item.href}
                className={`inline-flex items-center gap-2 rounded-full border px-4 py-2 text-sm font-medium whitespace-nowrap transition-colors ${
                  active
                    ? 'border-brand-500/40 bg-brand-400/10 text-brand-200'
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
