'use client';

import { useRouter } from 'next/navigation';
import { useRef, useState } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { Textarea } from '@/components/ui/textarea';
import { respondToReview } from '@/lib/api-client-helpers/reviews-client';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * Review-reply island (T-0117, AC-3..AC-5) — the only client code in the
 * route. A `Textarea` (≤500-char UX mirror, pre-filled with the existing
 * reply when editing) + submit, wired to `respondToReview`. On success
 * `router.refresh()` re-renders the SSR list with the new (overwritten,
 * Q4) reply; on failure an inline i18n-keyed `Alert` and the submit
 * re-enables (the SSR list stays rendered). Mirrors the `order-actions.tsx`
 * mutation pattern + the `MarkDeliveredButton` re-entrancy guard. The
 * backend `ReviewReplyTooLong` rule stays authoritative.
 */

const MAX_REPLY = 500;

interface ReplyFormProps {
  readonly reviewId: string;
  /** Pre-fill when editing an existing reply; empty for a first reply. */
  readonly initialReply?: string;
}

export function ReplyForm({ reviewId, initialReply = '' }: ReplyFormProps) {
  const router = useRouter();
  const [reply, setReply] = useState(initialReply);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inFlightRef = useRef(false);

  async function handleSubmit() {
    const trimmed = reply.trim();
    if (inFlightRef.current || trimmed.length === 0) return;
    inFlightRef.current = true;
    setSubmitting(true);
    setError(null);

    const result = await respondToReview(reviewId, trimmed);
    if (result.success) {
      // Server re-render shows the overwritten reply (Q4 — one reply).
      router.refresh();
      return;
    }

    setError(resolveErrorMessage(result.error));
    inFlightRef.current = false;
    setSubmitting(false);
  }

  return (
    <div className="flex flex-col gap-3">
      {error ? <Alert variant="error">{error}</Alert> : null}

      <div className="flex flex-col gap-1.5">
        <Textarea
          label={t('dashboard.maker.reviews.reply.label')}
          rows={3}
          maxLength={MAX_REPLY}
          placeholder={t('dashboard.maker.reviews.reply.placeholder')}
          value={reply}
          disabled={submitting}
          onChange={(event) => setReply(event.target.value)}
        />
        <p className="self-end text-xs text-zinc-500">
          {t('dashboard.maker.reviews.reply.hint')}
        </p>
      </div>

      <Button
        type="button"
        size="md"
        loading={submitting}
        disabled={reply.trim().length === 0 || submitting}
        onClick={() => void handleSubmit()}
        className="self-start"
      >
        {!submitting ? <Icon name="send" size={14} /> : null}
        {submitting
          ? t('dashboard.maker.reviews.reply.submitting')
          : t('dashboard.maker.reviews.reply.submit')}
      </Button>
    </div>
  );
}
