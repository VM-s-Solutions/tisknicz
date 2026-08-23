import { Avatar } from '@/components/ui/avatar';
import { EmptyState } from '@/components/ui/empty-state';
import { t } from '@/lib/i18n';
import { buildAvatarUrl, type MakerReviewItem } from '@/lib/api-client-helpers/catalog';
import { Stars } from '@/components/ui/stars';
import { formatDate } from '@/lib/utils/dates';

interface ReviewsSectionProps {
  readonly reviews: readonly MakerReviewItem[];
  /** True total from the maker's rating stats — the list itself is capped. */
  readonly totalCount: number;
}

/**
 * Reviews section (T-0047 AC-7, list body bound by T-0050). Renders the
 * latest 5 reviews newest-first with the maker's reply panel when one
 * exists; the empty-state copy stays for makers without reviews. One
 * bordered box: header row with a count, review rows divided by
 * hairlines — no nested cards.
 */
export function ReviewsSection({ reviews, totalCount }: ReviewsSectionProps) {
  return (
    <section className="rounded-xl border border-zinc-800 bg-surface-card">
      <div className="flex items-center justify-between gap-3 rounded-t-xl border-b border-zinc-800 bg-surface-secondary/60 px-4 py-3">
        <h2 className="text-sm font-semibold text-zinc-100">
          {t('catalog.maker.reviews.heading')}
        </h2>
        {/* T-0171 (audit PUB-M4): the badge showed `reviews.length`, which is
            capped at 5, while the seller panel showed the real ratingCount on
            the same screen — two numbers contradicting each other. Show the
            true total, and say so when the list below is only a slice. */}
        {totalCount > 0 ? (
          <span className="rounded-md bg-zinc-800 px-1.5 py-0.5 text-xs font-medium text-zinc-400">
            {totalCount}
          </span>
        ) : null}
      </div>
      {reviews.length === 0 ? (
        <div className="p-4">
          <EmptyState icon="star" title={t('catalog.maker.reviews.empty')} />
        </div>
      ) : (
        <ul className="divide-y divide-zinc-800">
          {reviews.map((review) => (
            <li key={review.reviewId} className="flex gap-3 px-4 py-4">
              {/*
                Reviews carry no author name by design (GDPR data
                minimisation), so the tile has no initials to fall back
                on and shows the generic user glyph. Rendering it even
                without an avatar keeps every row on the same text
                baseline instead of ragging left when one reviewer has a
                picture and the next doesn't.
              */}
              <Avatar src={buildAvatarUrl(review.authorAvatarBlobPath)} size="sm" />
              <div className="flex min-w-0 flex-1 flex-col gap-2">
                <div className="flex items-center justify-between gap-3">
                  <Stars value={review.ratingStars} />
                  <time className="text-xs text-zinc-500" dateTime={review.createdAt}>
                    {formatDate(review.createdAt)}
                  </time>
                </div>
                {review.comment ? (
                  <p className="break-words text-sm text-zinc-300">{review.comment}</p>
                ) : null}
                {review.replyBody ? (
                  <div className="mt-1 border-l-2 border-brand-500/40 pl-3">
                    <p className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
                      {t('catalog.maker.reviews.reply_label')}
                    </p>
                    <p className="mt-1 break-words text-sm text-zinc-300">{review.replyBody}</p>
                  </div>
                ) : null}
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
