'use client';

import { useRouter } from 'next/navigation';
import { useState, useTransition } from 'react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import type { CompletePayoutBatchResult } from '@/lib/api-client-helpers/admin-ops-client';
import { Alert } from '@/components/ui/alert';
import { t } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { CompleteBatchModal } from './complete-batch-modal';
import { PayoutCsvDownload } from './payout-csv-download';
import { PayoutSettlementCard } from './payout-row';

/**
 * Payout by-id action surface (T-0118c §3). The only client island wrapper
 * for the payout route — it owns the batch-id entry (no list read, gap),
 * the mark-completed modal trigger, the operator CSV download, and the
 * settlement-facts card rendered from the complete response. The complete
 * modal carries the disabled-while-pending idempotency lock; the CSV is the
 * operator-only blob download (A.4). On a successful completion the page is
 * refreshed so the processing count reconciles against the server.
 */

export function PayoutActions() {
  const router = useRouter();
  const [batchId, setBatchId] = useState('');
  const [batchNumber, setBatchNumber] = useState('');
  const [modalOpen, setModalOpen] = useState(false);
  const [completed, setCompleted] = useState<CompletePayoutBatchResult | null>(null);
  const [, startTransition] = useTransition();

  const trimmedId = batchId.trim();
  const idValid = trimmedId !== '';

  function handleCompleted(result: CompletePayoutBatchResult) {
    setCompleted(result);
    startTransition(() => {
      router.refresh();
    });
  }

  return (
    <Card className="flex flex-col gap-4">
      <div>
        <h2 className="text-sm font-semibold text-zinc-200">
          {t('dashboard.admin.ops.payouts.actions.heading')}
        </h2>
        <p className="mt-1 text-sm text-zinc-400">
          {t('dashboard.admin.ops.payouts.actions.intro')}
        </p>
      </div>

      <Input
        label={t('dashboard.admin.ops.payouts.batchIdLabel')}
        value={batchId}
        onChange={(e) => setBatchId(e.target.value)}
        autoComplete="off"
        spellCheck={false}
      />
      <p className="-mt-2 text-xs text-zinc-500">{t('dashboard.admin.ops.payouts.batchIdHint')}</p>

      <Input
        label={t('dashboard.admin.ops.payouts.batchNumberLabel')}
        value={batchNumber}
        onChange={(e) => setBatchNumber(e.target.value)}
        autoComplete="off"
        spellCheck={false}
      />
      <p className="-mt-2 text-xs text-zinc-500">
        {t('dashboard.admin.ops.payouts.batchNumberHint')}
      </p>

      <div className="flex flex-wrap items-center gap-3">
        <Button
          type="button"
          disabled={!idValid}
          onClick={() => setModalOpen(true)}
        >
          <Icon name="checkCircle" size={16} />
          {t('dashboard.admin.ops.payouts.complete.button')}
        </Button>
      </div>

      <div className="border-t border-zinc-800 pt-4">
        <p className="mb-3 text-sm text-zinc-400">{t('dashboard.admin.ops.payouts.csv.intro')}</p>
        <PayoutCsvDownload batchId={trimmedId} batchNumber={batchNumber} />
      </div>

      {completed ? (
        <div className="border-t border-zinc-800 pt-4">
          <Alert variant="success" className="mb-3">
            <p className="text-sm">
              {t('dashboard.admin.ops.payouts.complete.success', {
                batchId: completed.batchId,
                total: formatCzk(completed.totalAmountMinor, completed.currency),
              })}
            </p>
          </Alert>
          <PayoutSettlementCard
            state={completed.state}
            totalAmountMinor={completed.totalAmountMinor}
            currency={completed.currency}
            orderCount={completed.orderCount}
            makerCount={completed.makerCount}
            bankReference={completed.bankReference}
            completedAt={
              completed.completedAt instanceof Date
                ? completed.completedAt.toISOString()
                : String(completed.completedAt)
            }
          />
        </div>
      ) : null}

      {modalOpen ? (
        <CompleteBatchModal
          batchId={trimmedId}
          onClose={() => setModalOpen(false)}
          onCompleted={handleCompleted}
        />
      ) : null}
    </Card>
  );
}
