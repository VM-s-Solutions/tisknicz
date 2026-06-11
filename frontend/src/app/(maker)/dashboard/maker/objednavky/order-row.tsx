import Link from 'next/link';
import { Badge } from '@/components/ui/badge';
import type { MakerOrderListItem } from '@/lib/api-client-helpers/maker-orders';
import { t } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { orderStateBadgeVariant, orderStateLabelKey } from '@/lib/orders/state-labels';
import { formatDate } from '@/lib/utils/dates';

/**
 * Presentational order rows for the maker dashboard list (T-0087a —
 * T-0086a customer order-row mirrored onto the maker columns). Server-
 * safe: pure formatting + links, no client logic. Two layouts: stacked
 * cards below `md`, a grid "table" at `md+` (the whole row is the
 * `<Link>` target per AC-9 — a real `<tr>` cannot be an anchor, so the
 * desktop layout is a CSS grid with a header row).
 *
 * GDPR surface (T-0081 §A.2): the row shows `customerContactName` only
 * — the DTO carries no email, and no `mailto:` is rendered anywhere
 * (AC-4). Money column is the maker's payout (T-0081 §C lock), never
 * the platform fee.
 */

const GRID_COLUMNS =
  'md:grid md:grid-cols-[7.5rem_8rem_minmax(0,1fr)_minmax(0,1fr)_6.5rem_6.5rem_4.5rem] md:items-center md:gap-4';

export function OrderRows({ items }: { readonly items: readonly MakerOrderListItem[] }) {
  return (
    <div className="flex flex-col gap-3 md:gap-0">
      <div
        className={`hidden border-b border-zinc-800 px-4 pb-3 text-xs font-semibold uppercase tracking-widest text-zinc-500 ${GRID_COLUMNS}`}
      >
        <span>{t('dashboard.maker.orders.table.number')}</span>
        <span>{t('dashboard.maker.orders.table.state')}</span>
        <span>{t('dashboard.maker.orders.table.customer')}</span>
        <span>{t('dashboard.maker.orders.table.product')}</span>
        <span className="md:text-right">{t('dashboard.maker.orders.table.payout')}</span>
        <span className="md:text-right">{t('dashboard.maker.orders.table.created')}</span>
        <span className="md:text-right">{t('dashboard.maker.orders.table.unread')}</span>
      </div>
      {items.map((item) => (
        <OrderRow key={item.orderId} item={item} />
      ))}
    </div>
  );
}

function OrderRow({ item }: { readonly item: MakerOrderListItem }) {
  const productLabel = item.productTitle ?? t('dashboard.maker.orders.customOrder');

  return (
    <Link
      href={`/dashboard/maker/objednavky/${encodeURIComponent(item.orderId)}`}
      className={`flex flex-col gap-3 rounded-2xl border border-zinc-800 bg-surface-card p-4 transition-colors hover:border-zinc-700 hover:bg-zinc-900 md:rounded-none md:border-x-0 md:border-t-0 md:border-b md:bg-transparent ${GRID_COLUMNS}`}
    >
      <div className="flex items-center justify-between gap-3 md:contents">
        <span className="text-sm font-semibold text-zinc-100">{item.orderNumber}</span>
        <span>
          <Badge variant={orderStateBadgeVariant(item.state)}>
            {t(orderStateLabelKey(item.state))}
          </Badge>
        </span>
        <span className="hidden truncate text-sm text-zinc-300 md:block">
          {item.customerContactName}
        </span>
        <span className="hidden truncate text-sm text-zinc-300 md:block">{productLabel}</span>
        <span className="hidden text-sm text-zinc-100 md:block md:text-right">
          {formatCzk(item.makerPayoutAmountMinor, item.currency)}
        </span>
        <span className="hidden text-sm text-zinc-400 md:block md:text-right">
          {formatDate(item.createdAt)}
        </span>
        <span className="hidden md:flex md:justify-end">
          <UnreadBadge count={item.unreadMessageCount} />
        </span>
      </div>

      {/* Mobile-only detail block — hidden at md+ where the grid columns above render. */}
      <div className="flex flex-col gap-1 md:hidden">
        <p className="truncate text-sm text-zinc-300">{item.customerContactName}</p>
        <p className="truncate text-sm text-zinc-400">{productLabel}</p>
        <div className="mt-1 flex items-center justify-between gap-3">
          <span className="text-sm text-zinc-400">{formatDate(item.createdAt)}</span>
          <span className="flex items-center gap-2">
            <UnreadBadge count={item.unreadMessageCount} />
            <span className="text-sm font-semibold text-zinc-100">
              {formatCzk(item.makerPayoutAmountMinor, item.currency)}
            </span>
          </span>
        </div>
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
    <Badge variant="brand" aria-label={t('dashboard.maker.orders.unreadAria', { count })}>
      {count}
    </Badge>
  );
}
