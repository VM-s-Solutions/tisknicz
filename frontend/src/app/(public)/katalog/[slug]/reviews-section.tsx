import { Card } from '@/components/ui/card';
import { t } from '@/lib/i18n';
import type { MakerReviewItem } from '@/lib/api-client-helpers/catalog';
import { Stars } from './stars';

interface ReviewsSectionProps {
  readonly reviews: readonly MakerReviewItem[];
}

/**
 * Reviews section (T-0047 AC-7, list body bound by T-0050). Renders the
 * latest 5 reviews newest-first with the maker's reply panel when one
 * exists; the empty-state copy stays for makers without reviews.
 */
export function ReviewsSection({ reviews }: ReviewsSectionProps) {
  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-xl font-semibold text-white">{t('catalog.maker.reviews.heading')}</h2>
      {reviews.length === 0 ? (
        <Card padding="md">
          <p className="text-sm text-zinc-400">{t('catalog.maker.reviews.empty')}</p>
        </Card>
      ) : (
        <ul className="flex flex-col gap-3">
          {reviews.map((review) => (
            <li key={review.reviewId}>
              <Card padding="md" className="flex flex-col gap-2">
                <div className="flex items-center gap-2">
                  <Stars value={review.ratingStars} />
                  <time
                    className="text-xs text-zinc-500"
                    dateTime={review.createdAt}
                  >
                    {new Date(review.createdAt).toLocaleDateString('cs-CZ')}
                  </time>
                </div>
                {review.comment ? (
                  <p className="text-sm text-zinc-300">{review.comment}</p>
                ) : null}
                {review.replyBody ? (
                  <div className="mt-1 border-l-2 border-zinc-700 pl-3">
                    <p className="text-xs font-semibold uppercase tracking-wide text-zinc-400">
                      {t('catalog.maker.reviews.reply_label')}
                    </p>
                    <p className="mt-1 text-sm text-zinc-300">{review.replyBody}</p>
                  </div>
                ) : null}
              </Card>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
