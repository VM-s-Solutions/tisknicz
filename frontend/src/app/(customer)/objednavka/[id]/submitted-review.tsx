import { Card } from '@/components/ui/card';
import { StarRating } from '@/components/ui/star-rating';
import type { SubmittedReview } from '@/lib/api-client-helpers/reviews-client';
import { t } from '@/lib/i18n';
import { formatDate } from '@/lib/utils/dates';

/**
 * Server-safe read-only render of a submitted review (T-0115, AC-5/AC-6):
 * the display star row + the optional comment (only when non-empty) + the
 * Czech-formatted submission date + a maker-reply panel rendered ONLY when
 * a reply exists. No interactivity, no edit/delete/re-submit affordance —
 * customer reviews are immutable (Q4); the only thing that ever changes is
 * the maker's reply appearing, authored entirely on the maker side.
 */

/**
 * Wire-honest presence check: the generated type declares
 * `string | undefined`, but ASP.NET serialises absent values as JSON
 * `null` — `typeof` covers both shapes.
 */
function hasText(value: string | undefined): value is string {
  return typeof value === 'string' && value !== '';
}

export function SubmittedReview({ review }: { readonly review: SubmittedReview }) {
  return (
    <Card padding="md" className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-sm font-semibold text-zinc-400">
          {t('customer.review.submittedHeading')}
        </h2>
        <span className="text-xs text-zinc-500">
          {t('customer.review.submittedOn', { date: formatDate(review.createdAt) })}
        </span>
      </div>

      <StarRating value={review.rating} size="md" />

      {hasText(review.body) ? (
        <p className="whitespace-pre-wrap text-sm text-zinc-200">{review.body}</p>
      ) : null}

      {hasText(review.makerReply) ? (
        <div className="mt-1 rounded-xl border border-zinc-800 bg-surface-elevated px-4 py-3">
          <h3 className="text-xs font-semibold text-zinc-400">
            {t('customer.review.makerReplyHeading')}
          </h3>
          <p className="mt-1 whitespace-pre-wrap text-sm text-zinc-200">{review.makerReply}</p>
        </div>
      ) : null}
    </Card>
  );
}
