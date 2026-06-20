'use client';

import { useRouter } from 'next/navigation';
import { useRef, useState, useTransition } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { Textarea } from '@/components/ui/textarea';
import {
  acknowledgeOutboxEvent,
  retryOutboxEvent,
} from '@/lib/api-client-helpers/admin-ops-client';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * Per-row outbox triage actions (T-0127 §8). The client island for one
 * stalled-event row — it owns the retry + acknowledge actions (the T-0109
 * endpoints) keyed on the row's VISIBLE id (no more blind paste). RETRY is a
 * one-shot nudge (no body); retry-on-processed surfaces the
 * `outbox.alreadyProcessed` 409 as a clear "already ran" alert (NOT a silent
 * success). ACKNOWLEDGE captures a mandatory reason (the backend Validator
 * caps it at 2000 chars — authoritative) behind a toggle so the row stays
 * compact. Every mutation button is disabled-while-pending; results/errors
 * render inline via i18n keys + `resolveErrorMessage`. On success a
 * `router.refresh()` reconciles the list + count against the server.
 */

const REASON_MAX_LENGTH = 2000;

export function OutboxRowActions({ eventId }: { readonly eventId: string }) {
  const router = useRouter();
  const [ackOpen, setAckOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [isRefreshing, startTransition] = useTransition();
  const [submitting, setSubmitting] = useState<'retry' | 'ack' | null>(null);
  const inFlightRef = useRef(false);

  const trimmedReason = reason.trim();
  const reasonValid = trimmedReason !== '' && trimmedReason.length <= REASON_MAX_LENGTH;
  const busy = submitting !== null || isRefreshing;

  function refresh() {
    startTransition(() => {
      router.refresh();
    });
  }

  async function handleRetry() {
    if (inFlightRef.current || busy) return;
    inFlightRef.current = true;
    setSubmitting('retry');
    setError(null);
    setSuccess(null);

    const result = await retryOutboxEvent(eventId);

    if (result.success) {
      setSuccess(
        t('dashboard.admin.ops.outbox.retry.success', { retryCount: result.value.retryCount }),
      );
      refresh();
    } else {
      setError(resolveErrorMessage(result.error));
    }

    inFlightRef.current = false;
    setSubmitting(null);
  }

  async function handleAcknowledge() {
    if (inFlightRef.current || !reasonValid || busy) return;
    inFlightRef.current = true;
    setSubmitting('ack');
    setError(null);
    setSuccess(null);

    const result = await acknowledgeOutboxEvent(eventId, trimmedReason);

    if (result.success) {
      setSuccess(t('dashboard.admin.ops.outbox.ack.success'));
      refresh();
    } else {
      setError(resolveErrorMessage(result.error));
    }

    inFlightRef.current = false;
    setSubmitting(null);
  }

  return (
    <div className="flex flex-col gap-3">
      {error ? <Alert variant="error">{error}</Alert> : null}
      {success ? <Alert variant="success">{success}</Alert> : null}

      <div className="flex flex-wrap items-center gap-3">
        <Button
          type="button"
          variant="secondary"
          loading={submitting === 'retry'}
          disabled={busy}
          onClick={() => void handleRetry()}
        >
          <Icon name="arrowRight" size={16} />
          {submitting === 'retry'
            ? t('dashboard.admin.ops.outbox.retry.pending')
            : t('dashboard.admin.ops.outbox.retry.button')}
        </Button>
        <Button
          type="button"
          variant="outline"
          disabled={busy}
          onClick={() => setAckOpen((open) => !open)}
        >
          <Icon name="check" size={16} />
          {t('dashboard.admin.ops.outbox.ack.toggle')}
        </Button>
      </div>

      {ackOpen ? (
        <div className="border-t border-zinc-800 pt-3">
          <Textarea
            rows={3}
            label={t('dashboard.admin.ops.outbox.ack.reasonLabel')}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            disabled={busy}
            maxLength={REASON_MAX_LENGTH}
          />
          <p className="mt-1 text-xs text-zinc-500">
            {t('dashboard.admin.ops.outbox.ack.reasonHint')}
          </p>
          <div className="mt-3 flex justify-end">
            <Button
              type="button"
              loading={submitting === 'ack'}
              disabled={!reasonValid || busy}
              onClick={() => void handleAcknowledge()}
            >
              {submitting === 'ack'
                ? t('dashboard.admin.ops.outbox.ack.pending')
                : t('dashboard.admin.ops.outbox.ack.button')}
            </Button>
          </div>
        </div>
      ) : null}
    </div>
  );
}
