import Link from 'next/link';
import { Badge } from '@/components/ui/badge';
import type { AdminOrderListItem } from '@/lib/api-client-helpers/admin-client';
import { t } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { orderStateBadgeVariant, orderStateLabelKey } from '@/lib/orders/state-labels';
import { formatDate } from '@/lib/utils/dates';

/**
 * Presentational admin order rows (T-0118a, mirroring the maker
 * objednavky order-row). Server-safe: pure formatting. As of T-0118b the
 * row IS a `<Link>` to the order detail route
 * (`/dashboard/admin/orders/[orderId]`) — slice b built that route, so
 * the slice-a forward-compat deferral (the row was non-Link to avoid a
 * 404) is now closed. Two layouts: stacked cards below `md`, a grid
 * "table" at `md+`.
 *
 * Operator surface: unlike the maker DTO, the admin row shows
 * `customerEmail` (T-0118a §"Why the admin sees customerEmail" — the
 * admin resolves disputes/refunds that require contacting the customer).
 */

const GRID_COLUMNS =
  'md:grid md:grid-cols-[7rem_7rem_minmax(0,1fr)_minmax(0,1fr)_4rem_6.5rem_7rem] md:items-center md:gap-4';

export function OrderRows({ items }: { readonly items: readonly AdminOrderListItem[] }) {
  return (
    <div className="flex flex-col gap-3 md:gap-0">
      <div
        className={`hidden border-b border-zinc-800 px-4 pb-3 text-xs font-semibold uppercase tracking-widest text-zinc-500 ${GRID_COLUMNS}`}
      >
        <span>{t('dashboard.admin.orders.table.number')}</span>
        <span>{t('dashboard.admin.orders.table.state')}</span>
        <span>{t('dashboard.admin.orders.table.maker')}</span>
        <span>{t('dashboard.admin.orders.table.customer')}</span>
        <span>{t('dashboard.admin.orders.table.country')}</span>
        <span className="md:text-right">{t('dashboard.admin.orders.table.created')}</span>
        <span className="md:text-right">{t('dashboard.admin.orders.table.total')}</span>
      </div>
      {items.map((item) => (
        <OrderRow key={item.orderId} item={item} />
      ))}
    </div>
  );
}

function OrderRow({ item }: { readonly item: AdminOrderListItem }) {
  return (
    <Link
      href={`/dashboard/admin/orders/${encodeURIComponent(item.orderId)}`}
      className={`flex flex-col gap-3 rounded-2xl border border-zinc-800 bg-surface-card p-4 transition-colors hover:border-zinc-700 hover:bg-zinc-800/40 md:rounded-none md:border-x-0 md:border-t-0 md:border-b md:bg-transparent md:p-4 md:hover:bg-zinc-800/40 ${GRID_COLUMNS}`}
    >
      <div className="flex items-center justify-between gap-3 md:contents">
        <span className="text-sm font-semibold text-zinc-100">{item.orderNumber}</span>
        <span>
          <Badge variant={orderStateBadgeVariant(item.state)}>
            {t(orderStateLabelKey(item.state))}
          </Badge>
        </span>
        <span className="hidden truncate text-sm text-zinc-300 md:block">{item.makerName}</span>
        <span className="hidden truncate text-sm text-zinc-300 md:block">{item.customerEmail}</span>
        <span className="hidden text-sm text-zinc-400 md:block">{item.countryCode}</span>
        <span className="hidden text-sm text-zinc-400 md:block md:text-right">
          {formatDate(item.createdAt)}
        </span>
        <span className="hidden text-sm font-semibold text-zinc-100 md:block md:text-right">
          {formatCzk(item.totalAmountMinor, item.currency)}
        </span>
      </div>

      {/* Mobile-only detail block — hidden at md+ where the grid columns render. */}
      <div className="flex flex-col gap-1 md:hidden">
        <p className="truncate text-sm text-zinc-300">{item.makerName}</p>
        <p className="truncate text-sm text-zinc-400">{item.customerEmail}</p>
        <div className="mt-1 flex items-center justify-between gap-3">
          <span className="text-sm text-zinc-400">
            {item.countryCode} · {formatDate(item.createdAt)}
          </span>
          <span className="text-sm font-semibold text-zinc-100">
            {formatCzk(item.totalAmountMinor, item.currency)}
          </span>
        </div>
      </div>
    </Link>
  );
}
