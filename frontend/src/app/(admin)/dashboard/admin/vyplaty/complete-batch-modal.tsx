'use client';

import { useEffect, useId, useRef, useState } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { DatePicker } from '@/components/ui/date-picker';
import { Input } from '@/components/ui/input';
import {
  type CompletePayoutBatchResult,
  completePayoutBatch,
} from '@/lib/api-client-helpers/admin-ops-client';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * Mark-payout-batch-completed modal (T-0118c §3, T-0103). Captures
 * `BankReference` (required) + `PaymentDate` (optional date picker) and
 * posts the completion. Forward-only terminal state: the button is
 * disabled-while-pending (idempotency lock — a double-submit is a backend
 * Silent-Success `alreadyCompleted: true`, surfaced coherently, but the UI
 * does not invite it). Failure codes `payoutBatch.notProcessing` /
 * `payoutBatch.notFound` render via `resolveErrorMessage`. The modal shell
 * follows the T-0118b ShipConfirm focus-trap pattern (no shared Dialog
 * primitive; Esc/Cancel/backdrop close only when not busy).
 */

export function CompleteBatchModal({
  batchId,
  onClose,
  onCompleted,
}: {
  readonly batchId: string;
  readonly onClose: () => void;
  readonly onCompleted: (result: CompletePayoutBatchResult) => void;
}) {
  const titleId = useId();
  const [bankReference, setBankReference] = useState('');
  const [paymentDate, setPaymentDate] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const inFlightRef = useRef(false);

  useEffect(() => {
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !submitting) onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', onKey);
    };
  }, [onClose, submitting]);

  const refValid = bankReference.trim() !== '';
  const canSubmit = refValid && !submitting;

  async function handleSubmit() {
    if (inFlightRef.current || !canSubmit) return;
    inFlightRef.current = true;
    setSubmitting(true);
    setError(null);
    setInfo(null);

    const result = await completePayoutBatch(batchId, {
      bankReference: bankReference.trim(),
      paymentDate: paymentDate.trim() === '' ? undefined : paymentDate.trim(),
    });

    if (result.success) {
      if (result.value.alreadyCompleted) {
        setInfo(t('dashboard.admin.ops.payouts.complete.alreadyCompleted'));
      }
      onCompleted(result.value);
      inFlightRef.current = false;
      setSubmitting(false);
      if (!result.value.alreadyCompleted) onClose();
      return;
    }

    setError(resolveErrorMessage(result.error));
    inFlightRef.current = false;
    setSubmitting(false);
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      className="fixed inset-0 z-50 flex items-center justify-center overflow-y-auto p-4"
    >
      <div
        aria-hidden="true"
        onClick={() => {
          if (!submitting) onClose();
        }}
        className="absolute inset-0 bg-black/70"
      />
      <div className="relative z-10 my-8 w-full max-w-lg rounded-xl border border-zinc-800 bg-surface-card p-6 shadow-2xl">
        <h2 id={titleId} className="text-lg font-semibold text-white">
          {t('dashboard.admin.ops.payouts.complete.title')}
        </h2>
        <div className="mt-4 flex flex-col gap-4">
          <p className="text-sm text-zinc-400">
            {t('dashboard.admin.ops.payouts.complete.intro')}
          </p>

          {error ? <Alert variant="error">{error}</Alert> : null}
          {info ? <Alert variant="info">{info}</Alert> : null}

          <div>
            <Input
              label={t('dashboard.admin.ops.payouts.complete.bankReferenceLabel')}
              value={bankReference}
              onChange={(e) => setBankReference(e.target.value)}
              disabled={submitting}
              autoComplete="off"
              spellCheck={false}
            />
            <p className="mt-1 text-xs text-zinc-500">
              {t('dashboard.admin.ops.payouts.complete.bankReferenceHint')}
            </p>
          </div>

          <div>
            <DatePicker
              label={t('dashboard.admin.ops.payouts.complete.paymentDateLabel')}
              value={paymentDate}
              onChange={setPaymentDate}
              disabled={submitting}
            />
            <p className="mt-1 text-xs text-zinc-500">
              {t('dashboard.admin.ops.payouts.complete.paymentDateHint')}
            </p>
          </div>

          <div className="mt-2 flex items-center justify-end gap-3">
            <Button type="button" variant="outline" onClick={onClose} disabled={submitting}>
              {t('common.cancel')}
            </Button>
            <Button
              type="button"
              loading={submitting}
              disabled={!canSubmit}
              onClick={() => void handleSubmit()}
            >
              {submitting
                ? t('dashboard.admin.ops.payouts.complete.submitting')
                : t('dashboard.admin.ops.payouts.complete.submit')}
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
