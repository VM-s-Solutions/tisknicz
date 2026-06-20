import { PayoutBatchState } from '@/lib/api-client-helpers/admin-ops-client';
import type { MessageKey } from '@/lib/i18n';

/**
 * Presentational payout-batch state → label/variant maps (T-0118c §3 /
 * T-0127). Server-safe pure formatting consumed by the browsable
 * payout-batch card (`payout-batch-card.tsx`). The state→label/variant map
 * is presentation routing over backend-supplied data, NOT a business rule
 * (Processing → "Zpracovává se" warning, Completed → "Vyplaceno" success);
 * the forward-only terminal transition is backend.
 */

export function payoutStateBadgeVariant(state: PayoutBatchState): 'success' | 'warning' {
  return state === PayoutBatchState.Completed ? 'success' : 'warning';
}

export function payoutStateLabelKey(state: PayoutBatchState): MessageKey {
  return state === PayoutBatchState.Completed
    ? 'dashboard.admin.ops.payouts.state.completed'
    : 'dashboard.admin.ops.payouts.state.processing';
}
