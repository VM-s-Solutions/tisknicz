import { Card } from '@/components/ui/card';
import { StarRating } from '@/components/ui/star-rating';
import type { MakerReceivedReview } from '@/lib/api-client-helpers/reviews-client';
import { t } from '@/lib/i18n';
import { formatDate } from '@/lib/utils/dates';
import { ReplyForm } from './reply-form';

/**
 * Server-rendered card per received review (T-0117, AC-2/AC-3). Shows the
 * read-only rating stars, the order number, the comment (or a muted
 * "bez komentáře" line when null), and the Czech-short created date. When
 * a reply already exists it renders read-only (reply text + answered
 * date) ABOVE the edit-in-place form pre-filled with the current reply
 * (Q4 — one overwritable reply); when no reply exists, just the form. The
 * customer's rating + comment are read-only DOM — the maker can only
 * author their own reply.
 */

function hasText(value: string | undefined): value is string {
  return typeof value === 'string' && value !== '';
}

export function ReviewCard({ review }: { readonly review: MakerReceivedReview }) {
  const hasReply = hasText(review.makerReply);

  return (
    <Card padding="md" className="flex flex-col gap-4">
      <div className="flex flex-col gap-2">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <StarRating value={review.rating} size="md" />
          <span className="text-xs text-zinc-500">{formatDate(review.createdAt)}</span>
        </div>
        <p className="text-xs text-zinc-500">
          {t('dashboard.maker.reviews.card.order', { orderNumber: review.orderNumber })}
        </p>
      </div>

      {hasText(review.body) ? (
        <p className="whitespace-pre-wrap text-sm text-zinc-200">{review.body}</p>
      ) : (
        <p className="text-sm italic text-zinc-500">
          {t('dashboard.maker.reviews.card.noComment')}
        </p>
      )}

      <div className="rounded-xl border border-zinc-800 bg-surface-elevated px-4 py-3">
        {hasReply ? (
          <div className="mb-3 flex flex-col gap-1">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h3 className="text-xs font-semibold text-zinc-400">
                {t('dashboard.maker.reviews.reply.heading')}
              </h3>
              {hasText(review.makerReplyAt) ? (
                <span className="text-xs text-zinc-500">
                  {t('dashboard.maker.reviews.reply.answeredOn', {
                    date: formatDate(review.makerReplyAt),
                  })}
                </span>
              ) : null}
            </div>
            <p className="whitespace-pre-wrap text-sm text-zinc-200">{review.makerReply}</p>
          </div>
        ) : null}

        <ReplyForm
          reviewId={review.reviewId}
          initialReply={hasReply ? review.makerReply : ''}
        />
      </div>
    </Card>
  );
}
