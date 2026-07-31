import Link from 'next/link';
import { Badge } from '@/components/ui/badge';
import { Icon } from '@/components/ui/icon';
import { Tooltip } from '@/components/ui/tooltip';
import type { CustomerOrderListItem } from '@/lib/api-client-helpers/orders-client';
import { t } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { orderStateBadgeVariant, orderStateLabelKey } from '@/lib/orders/state-labels';
import { formatDate } from '@/lib/utils/dates';

/**
 * Presentational order list box for the customer dashboard (T-0086a).
 * Server-safe — pure formatting + links, no client logic. GitHub-style
 * container: one hairline-bordered box with a quiet header row (count)
 * and hairline-divided full-width rows; the whole row is the `<Link>`
 * target per AC-9, with a right chevron affordance and a surface-tint
 * hover (color-only feedback, no motion).
 */

export function OrderRows({
  items,
  totalCount,
}: {
  readonly items: readonly CustomerOrderListItem[];
  readonly totalCount: number;
}) {
  return (
    <div className="overflow-hidden rounded-xl border border-zinc-800 bg-surface-card">
      <div className="border-b border-zinc-800 bg-surface-secondary/60 px-4 py-3 sm:px-5">
        <h2 className="text-xs font-semibold tracking-widest text-zinc-500 uppercase">
          {t('customer.orders.count', { count: totalCount })}
        </h2>
      </div>
      <ul className="divide-y divide-zinc-800">
        {items.map((item) => (
          <li key={item.orderId}>
            <OrderRow item={item} />
          </li>
        ))}
      </ul>
    </div>
  );
}

function OrderRow({ item }: { readonly item: CustomerOrderListItem }) {
  const productLabel = item.productTitle ?? t('customer.orders.customOrder');

  return (
    <Link
      href={`/objednavka/${encodeURIComponent(item.orderId)}`}
      className="group flex flex-col gap-3 px-4 py-4 transition-colors hover:bg-surface-secondary/60 sm:px-5"
    >
      <div className="flex items-center justify-between gap-3">
        <div className="flex min-w-0 flex-wrap items-center gap-2.5">
          <span className="text-sm font-semibold text-zinc-100">{item.orderNumber}</span>
          <Badge variant={orderStateBadgeVariant(item.state)}>
            {t(orderStateLabelKey(item.state))}
          </Badge>
          <UnreadBadge count={item.unreadMessageCount} />
        </div>
        <Icon
          name="chevronRight"
          size={18}
          className="shrink-0 text-zinc-500 transition-colors group-hover:text-zinc-300"
        />
      </div>

      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div className="flex min-w-0 flex-col gap-1.5">
          <p className="truncate text-base font-semibold text-zinc-100">{productLabel}</p>
          <p className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-zinc-400">
            <span className="inline-flex min-w-0 items-center gap-1.5">
              <Icon name="user" size={14} className="shrink-0 text-zinc-500" />
              <span className="truncate">{item.makerName}</span>
            </span>
            <span className="inline-flex items-center gap-1.5">
              <Icon name="calendar" size={14} className="shrink-0 text-zinc-500" />
              {formatDate(item.createdAt)}
            </span>
          </p>
        </div>
        <p className="shrink-0 text-base font-semibold text-zinc-100">
          {formatCzk(item.totalAmountMinor, item.currency)}
        </p>
      </div>
    </Link>
  );
}

/**
 * Unread-message badge (Q7 lock) — pure read of the T-0089-projected
 * `unreadMessageCount`; hidden at 0. Numeric badge + accessible label
 * keeps the copy plural-neutral (patterns.md B.18).
 */
function UnreadBadge({ count }: { readonly count: number }) {
  if (count <= 0) {
    return null;
  }
  return (
    <Tooltip content={t('customer.orders.unreadAria', { count })}>
      <Badge variant="brand" aria-label={t('customer.orders.unreadAria', { count })}>
        {count}
      </Badge>
    </Tooltip>
  );
}
