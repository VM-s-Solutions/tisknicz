import Link from 'next/link';
import { Badge } from '@/components/ui/badge';
import { Icon } from '@/components/ui/icon';
import type { CustomerOrderListItem } from '@/lib/api-client-helpers/orders-client';
import { t } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { orderStateBadgeVariant, orderStateLabelKey } from '@/lib/orders/state-labels';
import { formatDate } from '@/lib/utils/dates';

/**
 * Presentational order rows for the customer dashboard list (T-0086a).
 * Server-safe — pure formatting + links, no client logic. Each order is
 * a lifted card (`.panel .card-lift`, the whole card is the `<Link>`
 * target per AC-9): number + state badge on top, product/maker/date
 * meta below, the total prominent on the right and a chevron that
 * slides on hover.
 */

export function OrderRows({ items }: { readonly items: readonly CustomerOrderListItem[] }) {
  return (
    <ul className="flex flex-col gap-3">
      {items.map((item) => (
        <li key={item.orderId}>
          <OrderRow item={item} />
        </li>
      ))}
    </ul>
  );
}

function OrderRow({ item }: { readonly item: CustomerOrderListItem }) {
  const productLabel = item.productTitle ?? t('customer.orders.customOrder');

  return (
    <Link
      href={`/objednavka/${encodeURIComponent(item.orderId)}`}
      className="group panel card-lift flex flex-col gap-4 rounded-2xl border border-zinc-800 p-5"
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
          className="shrink-0 text-zinc-600 transition-transform duration-200 group-hover:translate-x-1 group-hover:text-brand-400"
        />
      </div>

      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div className="flex min-w-0 flex-col gap-1.5">
          <p className="truncate text-base font-semibold text-white">{productLabel}</p>
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
        <p className="shrink-0 text-lg font-semibold text-brand-400">
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
    <Badge variant="brand" aria-label={t('customer.orders.unreadAria', { count })}>
      {count}
    </Badge>
  );
}
