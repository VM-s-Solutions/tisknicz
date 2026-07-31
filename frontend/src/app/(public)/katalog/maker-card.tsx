import Link from 'next/link';
import { Avatar } from '@/components/ui/avatar';
import { Badge } from '@/components/ui/badge';
import { Icon } from '@/components/ui/icon';
import { Tooltip } from '@/components/ui/tooltip';
import {
  buildMakerLogoUrl,
  RATING_BP_PER_STAR,
  type MakerListItem,
} from '@/lib/api-client-helpers/catalog';
import { t } from '@/lib/i18n';

interface MakerCardProps {
  readonly item: MakerListItem;
}

/**
 * Server Component card for one <see cref="MakerListItem"/>. Links to
 * the maker profile page (T-0047). Rating math is display-only:
 * basis-points → 0.0-5.0 with one decimal.
 */
export function MakerCard({ item }: MakerCardProps) {
  const hasRating = item.ratingCount > 0;
  // RatingAverageBp is 0..50_000 basis points (10 000 bp per star, per
  // CatalogQueries.BpPerStar) → 0.0–5.0 stars.
  const ratingValue = hasRating ? item.ratingAverageBp / RATING_BP_PER_STAR : 0;
  const ratingDisplay = hasRating ? ratingValue.toFixed(1) : null;

  return (
    <Link
      href={`/katalog/${item.slug}`}
      className="group block h-full rounded-xl focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/40"
    >
      <article className="panel card-lift flex h-full flex-col gap-4 rounded-xl border border-zinc-800 p-5 sm:p-6">
        <div className="flex items-start gap-4">
          {/* Decorative: the <h3> beside it already names the maker. */}
          <Avatar src={buildMakerLogoUrl(item.logoBlobPath)} name={item.companyName} size="md" />

          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2.5">
              <h3 className="text-lg font-semibold text-zinc-100 group-hover:text-white">
                {item.companyName}
              </h3>
              {item.isVerified && (
                <Tooltip content={t('catalog.card.verified_tooltip')} className="shrink-0">
                  <Badge variant="brand" dot={false}>
                    {t('catalog.card.verified')}
                  </Badge>
                </Tooltip>
              )}
            </div>
            <p className="mt-1 flex items-center gap-1.5 text-sm text-zinc-500">
              <Icon name="mapPin" size={14} />
              <span className="truncate">{item.city}</span>
            </p>
          </div>
        </div>

        {item.bio && (
          <p className="line-clamp-2 text-sm leading-relaxed text-zinc-400">
            {item.bio}
          </p>
        )}

        <div className="mt-auto flex flex-wrap items-center justify-between gap-x-4 gap-y-2 border-t border-zinc-800/80 pt-4">
          <div className="flex items-center gap-1.5 text-sm whitespace-nowrap">
            {hasRating ? (
              <>
                <Icon name="star" size={14} className="text-amber-400" />
                <span className="font-semibold text-zinc-200">{ratingDisplay}</span>
                <span className="text-zinc-500">
                  {t('catalog.card.rating_count', { count: item.ratingCount })}
                </span>
              </>
            ) : (
              <>
                <Icon name="starOutline" size={14} className="text-zinc-500" />
                <span className="text-zinc-500">{t('catalog.card.rating_none')}</span>
              </>
            )}
          </div>

          <p className="flex items-center gap-1.5 text-xs text-zinc-500 whitespace-nowrap">
            <Icon name="shoppingBag" size={12} />
            {t('catalog.card.orders', { count: item.totalOrders })}
          </p>

          <span className="text-zinc-500 group-hover:text-brand-300">
            <Icon name="arrowRight" size={16} />
          </span>
        </div>
      </article>
    </Link>
  );
}
