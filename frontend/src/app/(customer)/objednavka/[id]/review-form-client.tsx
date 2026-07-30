'use client';

import { useRouter } from 'next/navigation';
import { useRef, useState } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { StarRating } from '@/components/ui/star-rating';
import { Textarea } from '@/components/ui/textarea';
import { submitReview } from '@/lib/api-client-helpers/reviews-client';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * Inline review-submission island (T-0115, AC-1..AC-5). Interactive
 * `StarRating` (1..5 required) + an optional `Textarea` (≤1000-char UX
 * mirror + live counter) + submit. The submit button stays disabled until
 * a star is picked or while a request is in flight. On success
 * `router.refresh()` re-renders the server tree into the read-only
 * `SubmittedReview` state (Q5 lock — no client store, no optimistic
 * mutation). On failure the mapped Czech error renders inline. The
 * `useRef` in-flight guard mirrors `MarkDeliveredButton`'s re-entrancy
 * protection. The backend stays authoritative for eligibility + rating
 * math + validation; the mirrors are UX-only.
 */

const MAX_BODY = 1000;

export function ReviewFormClient({ orderId }: { readonly orderId: string }) {
  const router = useRouter();
  const [rating, setRating] = useState(0);
  const [body, setBody] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inFlightRef = useRef(false);

  async function handleSubmit() {
    if (inFlightRef.current || rating < 1) return;
    inFlightRef.current = true;
    setSubmitting(true);
    setError(null);

    const trimmed = body.trim();
    const result = await submitReview(orderId, {
      rating,
      body: trimmed.length > 0 ? trimmed : undefined,
    });
    if (result.success) {
      // Server re-render swaps the form for the read-only block (Q5 lock).
      router.refresh();
      return;
    }

    setError(resolveErrorMessage(result.error));
    inFlightRef.current = false;
    setSubmitting(false);
  }

  return (
    <Card variant="elevated" padding="md" className="flex flex-col gap-4">
      <div className="flex flex-col gap-1">
        <h2 className="flex items-center gap-2 text-xs font-semibold tracking-widest text-zinc-500 uppercase">
          <Icon name="star" size={14} className="shrink-0" />
          {t('customer.review.heading')}
        </h2>
        <p className="text-sm text-zinc-300">{t('customer.review.prompt')}</p>
      </div>

      {error ? <Alert variant="error">{error}</Alert> : null}

      <StarRating
        value={rating}
        size="md"
        onChange={setRating}
        groupLabel={t('customer.review.ratingGroupLabel')}
        ariaLabelFor={(value) => t('customer.review.starLabel', { rating: value })}
        disabled={submitting}
      />

      <div className="flex flex-col gap-1.5">
        <Textarea
          label={t('customer.review.comment.label')}
          rows={4}
          maxLength={MAX_BODY}
          placeholder={t('customer.review.comment.placeholder')}
          value={body}
          disabled={submitting}
          onChange={(event) => setBody(event.target.value)}
        />
        <p className="self-end text-xs text-zinc-500" aria-live="polite">
          {t('customer.review.comment.counter', { count: body.length })}
        </p>
      </div>

      <Button
        type="button"
        size="lg"
        loading={submitting}
        disabled={rating < 1 || submitting}
        onClick={() => void handleSubmit()}
      >
        {submitting ? t('customer.review.submitting') : t('customer.review.submit')}
      </Button>
    </Card>
  );
}
