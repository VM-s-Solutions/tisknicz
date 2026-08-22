'use client';

import { useRouter } from 'next/navigation';
import { useRef, useState, useTransition } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { Textarea } from '@/components/ui/textarea';
import { respondToReview } from '@/lib/api-client-helpers/reviews-client';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * Review-reply island (T-0117, AC-3..AC-5; reworked in T-0174). A
 * `Textarea` (≤500-char UX mirror) + submit, wired to `respondToReview`.
 * On success `router.refresh()` re-renders the SSR list with the new
 * (overwritten, Q4) reply; on failure an inline i18n-keyed `Alert` and
 * the submit re-enables. The backend `ReviewReplyTooLong` rule stays
 * authoritative.
 *
 * T-0174 (audit MAKER-H1 + MAKER-L6): the success path previously never
 * reset `submitting`/`inFlightRef` — `router.refresh()` does not remount
 * the client island, so the form stayed disabled on "Odesílám…" forever.
 * The refresh now runs inside `useTransition` and the guards reset when
 * it settles. When a reply already exists the form also collapses behind
 * an "Upravit odpověď" toggle instead of sitting permanently open under
 * the reply it duplicates.
 */

const MAX_REPLY = 500;

interface ReplyFormProps {
  readonly reviewId: string;
  /** Pre-fill when editing an existing reply; empty for a first reply. */
  readonly initialReply?: string;
}

export function ReplyForm({ reviewId, initialReply = '' }: ReplyFormProps) {
  const router = useRouter();
  const hasExistingReply = initialReply.trim().length > 0;
  const [editing, setEditing] = useState(!hasExistingReply);
  const [reply, setReply] = useState(initialReply);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inFlightRef = useRef(false);
  const [isRefreshing, startRefresh] = useTransition();

  async function handleSubmit() {
    const trimmed = reply.trim();
    if (inFlightRef.current || trimmed.length === 0) return;
    inFlightRef.current = true;
    setSubmitting(true);
    setError(null);

    const result = await respondToReview(reviewId, trimmed);
    if (result.success) {
      // Server re-render shows the overwritten reply (Q4 — one reply).
      // The island is NOT remounted by the refresh, so the guards must
      // reset here or the form stays dead after a successful submit.
      startRefresh(() => {
        router.refresh();
      });
      inFlightRef.current = false;
      setSubmitting(false);
      setEditing(false);
      return;
    }

    setError(resolveErrorMessage(result.error));
    inFlightRef.current = false;
    setSubmitting(false);
  }

  if (!editing) {
    // Collapsed state: the SSR card above already shows the reply text;
    // this island only offers the way back into editing it.
    return (
      <Button
        type="button"
        variant="ghost"
        size="sm"
        className="self-start"
        disabled={isRefreshing}
        onClick={() => {
          setReply(initialReply);
          setError(null);
          setEditing(true);
        }}
      >
        <Icon name="edit" size={14} />
        {t('dashboard.maker.reviews.reply.edit')}
      </Button>
    );
  }

  const busy = submitting || isRefreshing;

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
          disabled={busy}
          onChange={(event) => setReply(event.target.value)}
        />
        <p className="self-end text-xs text-zinc-500">
          {t('dashboard.maker.reviews.reply.hint')}
        </p>
      </div>

      <div className="flex items-center gap-2">
        <Button
          type="button"
          size="md"
          loading={busy}
          disabled={reply.trim().length === 0 || busy}
          onClick={() => void handleSubmit()}
        >
          {!busy ? <Icon name="send" size={14} /> : null}
          {busy
            ? t('dashboard.maker.reviews.reply.submitting')
            : t('dashboard.maker.reviews.reply.submit')}
        </Button>
        {hasExistingReply ? (
          <Button
            type="button"
            variant="ghost"
            size="md"
            disabled={busy}
            onClick={() => {
              setReply(initialReply);
              setError(null);
              setEditing(false);
            }}
          >
            {t('dashboard.maker.reviews.reply.cancel')}
          </Button>
        ) : null}
      </div>
    </div>
  );
}
