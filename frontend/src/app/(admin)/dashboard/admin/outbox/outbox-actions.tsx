'use client';

import { useRouter } from 'next/navigation';
import { useRef, useState, useTransition } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import {
  acknowledgeOutboxEvent,
  retryOutboxEvent,
} from '@/lib/api-client-helpers/admin-ops-client';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * Outbox triage actions (T-0118c §1). The only client island in the
 * route — it owns the by-id retry + acknowledge actions (the T-0109
 * endpoints). RETRY is a one-shot nudge (no body); retry-on-processed
 * surfaces the `outbox.alreadyProcessed` 409 as a clear "already ran"
 * alert (NOT a silent success). ACKNOWLEDGE captures a mandatory reason
 * (the backend Validator caps it at 2000 chars — authoritative). Every
 * mutation button is disabled-while-pending; results/errors render inline
 * via i18n keys + `resolveErrorMessage`. On success a `router.refresh()`
 * reconciles the stalled count against the server (no optimistic UI).
 *
 * Because no event LIST read exists (gap), the operator pastes the event
 * id; the backend stays authoritative for whether the id exists / is
 * already processed.
 */

const REASON_MAX_LENGTH = 2000;

export function OutboxActions() {
  const router = useRouter();
  const [eventId, setEventId] = useState('');
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [isRefreshing, startTransition] = useTransition();
  const [submitting, setSubmitting] = useState<'retry' | 'ack' | null>(null);
  const inFlightRef = useRef(false);

  const trimmedId = eventId.trim();
  const trimmedReason = reason.trim();
  const idValid = trimmedId !== '';
  const reasonValid = trimmedReason !== '' && trimmedReason.length <= REASON_MAX_LENGTH;
  const busy = submitting !== null || isRefreshing;

  function refresh() {
    startTransition(() => {
      router.refresh();
    });
  }

  async function handleRetry() {
    if (inFlightRef.current || !idValid || busy) return;
    inFlightRef.current = true;
    setSubmitting('retry');
    setError(null);
    setSuccess(null);

    const result = await retryOutboxEvent(trimmedId);

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
    if (inFlightRef.current || !idValid || !reasonValid || busy) return;
    inFlightRef.current = true;
    setSubmitting('ack');
    setError(null);
    setSuccess(null);

    const result = await acknowledgeOutboxEvent(trimmedId, trimmedReason);

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
    <Card className="flex flex-col gap-4">
      <div>
        <h2 className="text-sm font-semibold text-zinc-200">
          {t('dashboard.admin.ops.outbox.actions.heading')}
        </h2>
        <p className="mt-1 text-sm text-zinc-400">
          {t('dashboard.admin.ops.outbox.actions.intro')}
        </p>
      </div>

      {error ? <Alert variant="error">{error}</Alert> : null}
      {success ? <Alert variant="success">{success}</Alert> : null}

      <Input
        label={t('dashboard.admin.ops.outbox.eventIdLabel')}
        value={eventId}
        onChange={(event) => setEventId(event.target.value)}
        disabled={busy}
        autoComplete="off"
        spellCheck={false}
      />
      <p className="-mt-2 text-xs text-zinc-500">{t('dashboard.admin.ops.outbox.eventIdHint')}</p>

      <div className="flex flex-wrap items-center gap-3">
        <Button
          type="button"
          variant="secondary"
          loading={submitting === 'retry'}
          disabled={!idValid || busy}
          onClick={() => void handleRetry()}
        >
          <Icon name="arrowRight" size={16} />
          {submitting === 'retry'
            ? t('dashboard.admin.ops.outbox.retry.pending')
            : t('dashboard.admin.ops.outbox.retry.button')}
        </Button>
      </div>

      <div className="mt-2 border-t border-zinc-800 pt-4">
        <Textarea
          rows={3}
          label={t('dashboard.admin.ops.outbox.ack.reasonLabel')}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          disabled={busy}
          maxLength={REASON_MAX_LENGTH}
        />
        <p className="mt-1 text-xs text-zinc-500">{t('dashboard.admin.ops.outbox.ack.reasonHint')}</p>
        <div className="mt-4 flex justify-end">
          <Button
            type="button"
            loading={submitting === 'ack'}
            disabled={!idValid || !reasonValid || busy}
            onClick={() => void handleAcknowledge()}
          >
            <Icon name="check" size={16} />
            {submitting === 'ack'
              ? t('dashboard.admin.ops.outbox.ack.pending')
              : t('dashboard.admin.ops.outbox.ack.button')}
          </Button>
        </div>
      </div>
    </Card>
  );
}
