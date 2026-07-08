'use client';

import { useRouter } from 'next/navigation';
import { useState, useTransition } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import {
  DisputeCategory,
  generateReturnLabel,
  markDisputeReturnReceived,
} from '@/lib/api-client-helpers/admin-orders';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * T-0146 admin dispute-review action: "Vygenerovat vratkový štítek" +
 * the admin-on-behalf-of-maker "mark received" acknowledgment
 * (US-customer-0023 AC-1 / AC-5). Renders ONLY when an open dispute
 * exists in a return-warranting category (AC-6 — the gate mirrors the
 * backend's `dispute.return.categoryNotEligible` check so the button
 * isn't offered for categories that would just 400).
 */

const RETURN_WARRANTING_CATEGORIES: readonly DisputeCategory[] = [
  DisputeCategory.DamagedItem,
  DisputeCategory.NotAsDescribed,
];

interface ReturnLabelFormProps {
  readonly disputeId: string | undefined;
  readonly disputeCategory: DisputeCategory | undefined;
  readonly returnLabelGenerated: boolean;
}

export function ReturnLabelForm({
  disputeId,
  disputeCategory,
  returnLabelGenerated,
}: ReturnLabelFormProps) {
  const router = useRouter();
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [isRefreshing, startTransition] = useTransition();

  if (!disputeId || !disputeCategory || !RETURN_WARRANTING_CATEGORIES.includes(disputeCategory)) {
    return null;
  }

  const busy = submitting || isRefreshing;

  async function handleGenerate() {
    if (busy || !disputeId) return;
    setSubmitting(true);
    setError(null);
    setNotice(null);

    const result = await generateReturnLabel(disputeId);
    setSubmitting(false);

    if (!result.success) {
      setError(resolveErrorMessage(result.error));
      return;
    }

    setNotice(t('dashboard.admin.orderActions.returnLabel.generated'));
    startTransition(() => {
      router.refresh();
    });
  }

  async function handleMarkReceived() {
    if (busy || !disputeId) return;
    setSubmitting(true);
    setError(null);
    setNotice(null);

    const result = await markDisputeReturnReceived(disputeId);
    setSubmitting(false);

    if (!result.success) {
      setError(resolveErrorMessage(result.error));
      return;
    }

    setNotice(t('dashboard.admin.orderActions.returnLabel.received'));
    startTransition(() => {
      router.refresh();
    });
  }

  return (
    <Card padding="md" className="flex flex-col gap-4">
      <h3 className="text-sm font-semibold text-zinc-200">
        {t('dashboard.admin.orderActions.returnLabel.heading')}
      </h3>

      {error ? <Alert variant="error">{error}</Alert> : null}
      {notice ? <Alert variant="success">{notice}</Alert> : null}

      <div className="flex flex-wrap items-center gap-3">
        <Button type="button" loading={busy} onClick={() => void handleGenerate()}>
          {busy
            ? t('dashboard.admin.orderActions.returnLabel.generating')
            : t('dashboard.admin.orderActions.returnLabel.generate')}
        </Button>

        {returnLabelGenerated ? (
          <Button
            type="button"
            variant="outline"
            loading={busy}
            onClick={() => void handleMarkReceived()}
          >
            {busy
              ? t('dashboard.admin.orderActions.returnLabel.markingReceived')
              : t('dashboard.admin.orderActions.returnLabel.markReceived')}
          </Button>
        ) : null}
      </div>
    </Card>
  );
}
