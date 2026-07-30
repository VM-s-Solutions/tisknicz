import Link from 'next/link';
import { t } from '@/lib/i18n';
import type { MessageKey } from '@/lib/i18n';

/**
 * Server-rendered state-tab strip for the maker order list (T-0087a
 * A.1 lock): Nové / Ve výrobě / Vše as `<Link>`s so every switch is a
 * full server re-fetch (Q5 SSR freshness) and deep links / back-forward
 * land on the right tab (AC-3). Sibling filter params (dates, sort,
 * pageSize) are preserved; `page` is intentionally reset — a different
 * tab has different pagination, and landing on an out-of-range page
 * would render a misleading empty state (filters-client precedent:
 * every filter change resets to page 1).
 */

export type OrderListTab = 'nove' | 'vyroba' | 'vse';

export const DEFAULT_ORDER_LIST_TAB: OrderListTab = 'nove';

const TAB_VALUES: readonly OrderListTab[] = ['nove', 'vyroba', 'vse'];

/** Missing/unknown `?tab=` values degrade to the default (T-0087a §C). */
export function parseOrderListTab(raw: string): OrderListTab {
  return (TAB_VALUES as readonly string[]).includes(raw)
    ? (raw as OrderListTab)
    : DEFAULT_ORDER_LIST_TAB;
}

const TAB_LABEL_KEYS: Record<OrderListTab, MessageKey> = {
  nove: 'dashboard.maker.orders.tab.nove',
  vyroba: 'dashboard.maker.orders.tab.vyroba',
  vse: 'dashboard.maker.orders.tab.vse',
};

interface OrderTabsProps {
  readonly activeTab: OrderListTab;
  /** Filter params to preserve across tab switches (dates / sort / pageSize — never `page` or `tab`). */
  readonly baseParams: Readonly<Record<string, string>>;
}

export function OrderTabs({ activeTab, baseParams }: OrderTabsProps) {
  const hrefFor = (tab: OrderListTab): string => {
    const sp = new URLSearchParams(baseParams);
    // Only non-default values are emitted (patterns.md B.8) — the
    // default Nové tab keeps a clean canonical URL.
    if (tab !== DEFAULT_ORDER_LIST_TAB) sp.set('tab', tab);
    const query = sp.toString();
    return query ? `/dashboard/maker/objednavky?${query}` : '/dashboard/maker/objednavky';
  };

  return (
    <nav
      className="inline-flex max-w-full items-center gap-1 overflow-x-auto rounded-full border border-zinc-800 bg-surface-card p-1"
      aria-label={t('dashboard.maker.orders.title')}
    >
      {TAB_VALUES.map((tab) => {
        const isActive = tab === activeTab;
        return (
          <Link
            key={tab}
            href={hrefFor(tab)}
            aria-current={isActive ? 'page' : undefined}
            className={`rounded-full border px-4 py-2 text-sm font-semibold whitespace-nowrap transition-colors ${
              isActive
                ? 'border-brand-500/40 bg-brand-400/10 text-brand-200'
                : 'border-transparent text-zinc-400 hover:bg-zinc-800/50 hover:text-zinc-200'
            }`}
          >
            {t(TAB_LABEL_KEYS[tab])}
          </Link>
        );
      })}
    </nav>
  );
}
