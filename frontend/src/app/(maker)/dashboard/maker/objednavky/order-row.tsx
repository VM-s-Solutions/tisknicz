import Link from 'next/link';
import { Badge } from '@/components/ui/badge';
import { Icon } from '@/components/ui/icon';
import { Tooltip } from '@/components/ui/tooltip';
import type { MakerOrderListItem } from '@/lib/api-client-helpers/maker-orders';
import { t } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { orderStateBadgeVariant, orderStateLabelKey } from '@/lib/orders/state-labels';
import { formatDate } from '@/lib/utils/dates';

/**
 * Presentational order list for the maker dashboard (T-0087a). Server-
 * safe: pure formatting + links, no client logic. GitHub "box" pattern:
 * one bordered container with a quiet count header, rows divided by
 * hairlines — the whole row is the `<Link>` target (AC-9): order number
 * + state badge, customer, product, created date, and the payout amount
 * prominent on the trailing edge with a chevron that picks up the brand
 * hue on hover (color only — static UI, no movement).
 *
 * GDPR surface (T-0081 §A.2): the row shows `customerContactName` only
 * — the DTO carries no email, and no `mailto:` is rendered anywhere
 * (AC-4). Money column is the maker's payout (T-0081 §C lock), never
 * the platform fee.
 */

interface OrderRowsProps {
  readonly items: readonly MakerOrderListItem[];
  readonly totalCount: number;
}

export function OrderRows({ items, totalCount }: OrderRowsProps) {
  return (
    <div className="overflow-hidden rounded-xl border border-zinc-800 bg-surface-card">
      <header className="border-b border-zinc-800 bg-surface-secondary/60 px-4 py-3 sm:px-5">
        <h2 className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
          {t('dashboard.maker.orders.count', { count: totalCount })}
        </h2>
      </header>
      <div className="divide-y divide-zinc-800">
        {items.map((item) => (
          <OrderRow key={item.orderId} item={item} />
        ))}
      </div>
    </div>
  );
}

function OrderRow({ item }: { readonly item: MakerOrderListItem }) {
  const productLabel = item.productTitle ?? t('dashboard.maker.orders.customOrder');

  return (
    <Link
      href={`/dashboard/maker/objednavky/${encodeURIComponent(item.orderId)}`}
      className="group flex flex-col gap-3 px-4 py-4 transition-colors hover:bg-surface-secondary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-brand-400 sm:flex-row sm:items-center sm:gap-6 sm:px-5"
    >
      <div className="flex min-w-0 flex-1 flex-col gap-1.5">
        <div className="flex flex-wrap items-center gap-2.5">
          <span className="text-sm font-bold text-zinc-100">{item.orderNumber}</span>
          <Badge variant={orderStateBadgeVariant(item.state)}>
            {t(orderStateLabelKey(item.state))}
          </Badge>
          <UnreadBadge count={item.unreadMessageCount} />
        </div>
        <p className="truncate text-sm text-zinc-300">{item.customerContactName}</p>
        <p className="truncate text-sm text-zinc-500">{productLabel}</p>
        <p className="flex items-center gap-1.5 text-xs text-zinc-500">
          <Icon name="calendar" size={13} className="shrink-0" />
          {formatDate(item.createdAt)}
        </p>
      </div>

      <div className="flex shrink-0 items-center justify-between gap-4 border-t border-zinc-800 pt-3 sm:border-t-0 sm:pt-0">
        <span className="text-lg font-bold text-zinc-100">
          {formatCzk(item.makerPayoutAmountMinor, item.currency)}
        </span>
        <span
          aria-hidden="true"
          className="text-zinc-500 transition-colors group-hover:text-brand-400"
        >
          <Icon name="chevronRight" size={18} />
        </span>
      </div>
    </Link>
  );
}

/**
 * Unread-message badge (Q7 lock) — pure read of the T-0079-denormalised
 * `unreadMessageCount`; `0`/`null`/`undefined` all collapse to "no
 * badge" (AC-5). Numeric badge + accessible label keeps the copy
 * plural-neutral (patterns.md B.18).
 */
function UnreadBadge({ count }: { readonly count: number | undefined }) {
  if (count === undefined || count <= 0) {
    return null;
  }
  return (
    <Tooltip content={t('dashboard.maker.orders.unreadAria', { count })}>
      <Badge variant="brand" aria-label={t('dashboard.maker.orders.unreadAria', { count })}>
        {count}
      </Badge>
    </Tooltip>
  );
}
