import Link from 'next/link';
import { Badge } from '@/components/ui/badge';
import { Icon } from '@/components/ui/icon';
import {
  type MakerPayoutListItem,
  PayoutBatchState,
} from '@/lib/api-client-helpers/payouts-client';
import { t } from '@/lib/i18n';
import type { MessageKey } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { formatDate } from '@/lib/utils/dates';

/**
 * Presentational payout-batch list for the maker dashboard (T-0116).
 * Server-safe: pure formatting + a row `<Link>` to the batch detail.
 * GitHub "box" pattern: one bordered container with a quiet count
 * header, rows divided by hairlines — each row carries a wallet icon
 * tile, the batch number + state badge, order count, date, and the
 * per-maker total prominent on the trailing edge. State maps to two
 * values only — Processing → "Připravujeme" (warning), Completed →
 * "Vyplaceno" (success); no `Pending`. The money column is the
 * per-maker total computed by the backend (formatCzk only formats).
 * NO CSV anywhere.
 */

/** Two-value state → badge variant / label (presentation routing, not a rule). */
function payoutStateBadgeVariant(state: PayoutBatchState): 'success' | 'warning' {
  return state === PayoutBatchState.Completed ? 'success' : 'warning';
}

function payoutStateLabelKey(state: PayoutBatchState): MessageKey {
  return state === PayoutBatchState.Completed
    ? 'dashboard.maker.payouts.state.completed'
    : 'dashboard.maker.payouts.state.processing';
}

interface PayoutRowsProps {
  readonly items: readonly MakerPayoutListItem[];
  readonly totalCount: number;
}

export function PayoutRows({ items, totalCount }: PayoutRowsProps) {
  return (
    <div className="overflow-hidden rounded-xl border border-zinc-800 bg-surface-card">
      <header className="border-b border-zinc-800 bg-surface-secondary/60 px-4 py-3 sm:px-5">
        <h2 className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
          {t('dashboard.maker.payouts.count', { count: totalCount })}
        </h2>
      </header>
      <div className="divide-y divide-zinc-800">
        {items.map((item) => (
          <PayoutRow key={item.batchId} item={item} />
        ))}
      </div>
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
      className="group flex flex-col gap-3 px-4 py-4 transition-colors hover:bg-surface-secondary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-brand-400 sm:flex-row sm:items-center sm:gap-5 sm:px-5"
    >
      <span className="icon-tile hidden h-11 w-11 shrink-0 sm:inline-flex" aria-hidden="true">
        <Icon name="wallet" size={20} />
      </span>

      <div className="flex min-w-0 flex-1 flex-col gap-1.5">
        <div className="flex flex-wrap items-center gap-2.5">
          <span className="min-w-0 break-words text-sm font-bold text-zinc-100">
            {item.batchNumber}
          </span>
          <Badge variant={payoutStateBadgeVariant(item.state)}>
            {t(payoutStateLabelKey(item.state))}
          </Badge>
        </div>
        <p className="text-sm text-zinc-400">
          {t('dashboard.maker.payouts.orderCount', { count: item.orderCount })}
        </p>
        <p className="flex items-center gap-1.5 text-xs text-zinc-500">
          <Icon name="calendar" size={13} className="shrink-0" />
          {dateLabel}
        </p>
      </div>

      <div className="flex shrink-0 items-center justify-between gap-4 border-t border-zinc-800 pt-3 sm:border-t-0 sm:pt-0">
        <span className="text-lg font-bold text-zinc-100">
          {formatCzk(item.makerTotalPaidMinor, item.currency)}
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
