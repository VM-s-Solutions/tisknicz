'use client';

import { useRouter } from 'next/navigation';
import { useState, useTransition } from 'react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { type AdminPayoutBatch, PayoutBatchState } from '@/lib/api-client-helpers/admin-ops-client';
import { t } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { formatDate } from '@/lib/utils/dates';
import { CompleteBatchModal } from './complete-batch-modal';
import { PayoutCsvDownload } from './payout-csv-download';
import { payoutStateBadgeVariant, payoutStateLabelKey } from './payout-row';

/**
 * Browsable payout-batch row (T-0127 §8). The client island for one batch
 * from the new LIST read — it renders the settlement facts (number, total,
 * maker/order count, created/completed dates) and the per-row actions keyed
 * on the batch's VISIBLE id: mark-completed (modal) for Processing batches +
 * the operator CSV download (always available). The complete modal carries
 * the disabled-while-pending idempotency lock; the CSV is the operator-only
 * cross-maker bank file (A.4). On a successful completion the page is
 * refreshed so the row state + the processing count reconcile against the
 * server. The forward-only terminal transition + the CSV readiness gate are
 * backend — this row only triggers the actions and surfaces the verdict.
 */

export function PayoutBatchCard({ batch }: { readonly batch: AdminPayoutBatch }) {
  const router = useRouter();
  const [modalOpen, setModalOpen] = useState(false);
  const [, startTransition] = useTransition();

  const isProcessing = batch.state === PayoutBatchState.Processing;

  function handleCompleted() {
    startTransition(() => {
      router.refresh();
    });
  }

  return (
    <div className="flex flex-col gap-4 p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-1">
          <span className="text-sm font-semibold text-zinc-100">{batch.batchNumber}</span>
          <span className="font-mono text-xs text-zinc-500">{batch.batchId}</span>
        </div>
        <Badge variant={payoutStateBadgeVariant(batch.state)}>
          {t(payoutStateLabelKey(batch.state))}
        </Badge>
      </div>

      <dl className="grid grid-cols-2 gap-x-8 gap-y-3 text-sm md:grid-cols-4">
        <div className="flex flex-col gap-1">
          <dt className="text-xs text-zinc-500">{t('dashboard.admin.ops.payouts.list.total')}</dt>
          <dd className="font-semibold text-zinc-100">
            {formatCzk(batch.totalAmountMinor, batch.currency)}
          </dd>
        </div>
        <div className="flex flex-col gap-1">
          <dt className="text-xs text-zinc-500">{t('dashboard.maker.payouts.table.orders')}</dt>
          <dd className="text-zinc-200">{batch.orderCount}</dd>
        </div>
        <div className="flex flex-col gap-1">
          <dt className="text-xs text-zinc-500">{t('dashboard.admin.nav.makers')}</dt>
          <dd className="text-zinc-200">{batch.makerCount}</dd>
        </div>
        <div className="flex flex-col gap-1">
          <dt className="text-xs text-zinc-500">
            {t('dashboard.admin.ops.payouts.list.createdAt')}
          </dt>
          <dd className="text-zinc-200">{formatDate(batch.createdAt)}</dd>
        </div>
        {batch.completedAt ? (
          <div className="flex flex-col gap-1">
            <dt className="text-xs text-zinc-500">
              {t('dashboard.admin.ops.payouts.list.completedAt')}
            </dt>
            <dd className="text-zinc-200">{formatDate(batch.completedAt)}</dd>
          </div>
        ) : null}
      </dl>

      <div className="flex flex-wrap items-center gap-3 border-t border-zinc-800 pt-4">
        {isProcessing ? (
          <Button type="button" onClick={() => setModalOpen(true)}>
            <Icon name="checkCircle" size={16} />
            {t('dashboard.admin.ops.payouts.complete.button')}
          </Button>
        ) : null}
        <PayoutCsvDownload batchId={batch.batchId} batchNumber={batch.batchNumber} />
      </div>

      {modalOpen ? (
        <CompleteBatchModal
          batchId={batch.batchId}
          onClose={() => setModalOpen(false)}
          onCompleted={handleCompleted}
        />
      ) : null}
    </div>
  );
}
