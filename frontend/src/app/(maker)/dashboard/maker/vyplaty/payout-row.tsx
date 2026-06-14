import Link from 'next/link';
import { Badge } from '@/components/ui/badge';
import {
  type MakerPayoutListItem,
  PayoutBatchState,
} from '@/lib/api-client-helpers/payouts-client';
import { t } from '@/lib/i18n';
import type { MessageKey } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { formatDate } from '@/lib/utils/dates';

/**
 * Presentational payout-batch rows for the maker dashboard list (T-0116,
 * mirroring the objednavky order-row). Server-safe: pure formatting + a row
 * `<Link>` to the batch detail. Two layouts: stacked cards below `md`, a
 * grid "table" at `md+` (the whole row is the `<Link>` target — a CSS grid,
 * not a `<tr>`). State maps to two values only — Processing → "Připravujeme"
 * (warning), Completed → "Vyplaceno" (success); no `Pending`. The money
 * column is the per-maker total computed by the backend (formatCzk only
 * formats). NO CSV anywhere.
 */

const GRID_COLUMNS =
  'md:grid md:grid-cols-[minmax(0,1fr)_8rem_6.5rem_7rem_7rem] md:items-center md:gap-4';

/** Two-value state → badge variant / label (presentation routing, not a rule). */
function payoutStateBadgeVariant(state: PayoutBatchState): 'success' | 'warning' {
  return state === PayoutBatchState.Completed ? 'success' : 'warning';
}

function payoutStateLabelKey(state: PayoutBatchState): MessageKey {
  return state === PayoutBatchState.Completed
    ? 'dashboard.maker.payouts.state.completed'
    : 'dashboard.maker.payouts.state.processing';
}

export function PayoutRows({ items }: { readonly items: readonly MakerPayoutListItem[] }) {
  return (
    <div className="flex flex-col gap-3 md:gap-0">
      <div
        className={`hidden border-b border-zinc-800 px-4 pb-3 text-xs font-semibold uppercase tracking-widest text-zinc-500 ${GRID_COLUMNS}`}
      >
        <span>{t('dashboard.maker.payouts.table.number')}</span>
        <span className="md:text-right">{t('dashboard.maker.payouts.table.total')}</span>
        <span className="md:text-right">{t('dashboard.maker.payouts.table.orders')}</span>
        <span>{t('dashboard.maker.payouts.table.state')}</span>
        <span className="md:text-right">{t('dashboard.maker.payouts.table.date')}</span>
      </div>
      {items.map((item) => (
        <PayoutRow key={item.batchId} item={item} />
      ))}
    </div>
  );
}

function PayoutRow({ item }: { readonly item: MakerPayoutListItem }) {
  const dateLabel = item.completedAt
    ? formatDate(item.completedAt)
    : t('dashboard.maker.payouts.datePlaceholder');

  return (
    <Link
      href={`/dashboard/maker/vyplaty/${encodeURIComponent(item.batchId)}`}
      className={`flex flex-col gap-3 rounded-2xl border border-zinc-800 bg-surface-card p-4 transition-colors hover:border-zinc-700 hover:bg-zinc-900 md:rounded-none md:border-x-0 md:border-t-0 md:border-b md:bg-transparent ${GRID_COLUMNS}`}
    >
      <div className="flex items-center justify-between gap-3 md:contents">
        <span className="text-sm font-semibold text-zinc-100">{item.batchNumber}</span>
        <span className="hidden text-sm font-semibold text-zinc-100 md:block md:text-right">
          {formatCzk(item.makerTotalPaidMinor, item.currency)}
        </span>
        <span className="hidden text-sm text-zinc-300 md:block md:text-right">
          {item.orderCount}
        </span>
        <span>
          <Badge variant={payoutStateBadgeVariant(item.state)}>
            {t(payoutStateLabelKey(item.state))}
          </Badge>
        </span>
        <span className="hidden text-sm text-zinc-400 md:block md:text-right">{dateLabel}</span>
      </div>

      {/* Mobile-only detail block — hidden at md+ where the grid columns above render. */}
      <div className="flex flex-col gap-1 md:hidden">
        <p className="text-sm text-zinc-400">
          {t('dashboard.maker.payouts.orderCount', { count: item.orderCount })}
        </p>
        <div className="mt-1 flex items-center justify-between gap-3">
          <span className="text-sm text-zinc-400">{dateLabel}</span>
          <span className="text-sm font-semibold text-zinc-100">
            {formatCzk(item.makerTotalPaidMinor, item.currency)}
          </span>
        </div>
      </div>
    </Link>
  );
}
