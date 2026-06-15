import { Badge } from '@/components/ui/badge';
import { PayoutBatchState } from '@/lib/api-client-helpers/admin-ops-client';
import { type MessageKey, t } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { formatDate } from '@/lib/utils/dates';

/**
 * Presentational payout-batch state badge + settlement facts (T-0118c §3).
 * Server-safe pure formatting. The state→label/variant map is presentation
 * routing over backend-supplied data, NOT a business rule (Processing →
 * "Zpracovává se" warning, Completed → "Vyplaceno" success); the
 * forward-only terminal transition is backend.
 *
 * No payout-batch LIST read exists on the contract (gap), so this component
 * does not render a row in a list — it renders the SETTLEMENT FACTS of a
 * single batch returned by the complete-action response (batch number,
 * total via `formatCzk`, maker/order count, settlement date +
 * `BankReference` when Completed). When the list endpoint lands, the same
 * card markup becomes the per-row presentation.
 */

export function payoutStateBadgeVariant(state: PayoutBatchState): 'success' | 'warning' {
  return state === PayoutBatchState.Completed ? 'success' : 'warning';
}

export function payoutStateLabelKey(state: PayoutBatchState): MessageKey {
  return state === PayoutBatchState.Completed
    ? 'dashboard.admin.ops.payouts.state.completed'
    : 'dashboard.admin.ops.payouts.state.processing';
}

interface PayoutSettlementCardProps {
  readonly state: PayoutBatchState;
  readonly totalAmountMinor: number;
  readonly currency: string;
  readonly orderCount: number;
  readonly makerCount: number;
  readonly bankReference: string;
  /** ISO 8601 settlement timestamp (Completed batches). */
  readonly completedAt: string;
}

export function PayoutSettlementCard({
  state,
  totalAmountMinor,
  currency,
  orderCount,
  makerCount,
  bankReference,
  completedAt,
}: PayoutSettlementCardProps) {
  return (
    <div className="flex flex-col gap-3 rounded-2xl border border-zinc-800 bg-zinc-900/60 p-4">
      <div className="flex items-center justify-between gap-3">
        <Badge variant={payoutStateBadgeVariant(state)}>{t(payoutStateLabelKey(state))}</Badge>
        <span className="text-sm font-semibold text-zinc-100">
          {formatCzk(totalAmountMinor, currency)}
        </span>
      </div>
      <dl className="grid grid-cols-2 gap-3 text-sm md:grid-cols-4">
        <div>
          <dt className="text-xs text-zinc-500">{t('dashboard.maker.payouts.table.orders')}</dt>
          <dd className="text-zinc-200">{orderCount}</dd>
        </div>
        <div>
          <dt className="text-xs text-zinc-500">{t('dashboard.admin.nav.makers')}</dt>
          <dd className="text-zinc-200">{makerCount}</dd>
        </div>
        <div>
          <dt className="text-xs text-zinc-500">
            {t('dashboard.admin.ops.payouts.complete.bankReferenceLabel')}
          </dt>
          <dd className="truncate text-zinc-200">{bankReference}</dd>
        </div>
        <div>
          <dt className="text-xs text-zinc-500">
            {t('dashboard.admin.ops.payouts.complete.paymentDateLabel')}
          </dt>
          <dd className="text-zinc-200">{formatDate(completedAt)}</dd>
        </div>
      </dl>
    </div>
  );
}
