import Image from 'next/image';
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

const LOGO_WIDTH = 400;
const LOGO_HEIGHT = 225;

/**
 * Server Component card for one <see cref="MakerListItem"/>. Links to
 * the maker profile page (T-0047). Rating math is display-only:
 * basis-points → 0.0-5.0 with one decimal.
 *
 * The logo is a full-bleed band across the top of the card with the
 * maker's details underneath — a brand mark reads at card width, not at
 * avatar width. It is `object-contain` on a flat surface rather than
 * `object-cover`: an uploaded logo is artwork with its own aspect ratio
 * and margins, so cropping it to fill would cut the wordmark. Makers
 * without a logo fall back to the shared initials tile so an empty and a
 * filled profile occupy the same box.
 */
export function MakerCard({ item }: MakerCardProps) {
  const hasRating = item.ratingCount > 0;
  // RatingAverageBp is 0..50_000 basis points (10 000 bp per star, per
  // CatalogQueries.BpPerStar) → 0.0–5.0 stars.
  const ratingValue = hasRating ? item.ratingAverageBp / RATING_BP_PER_STAR : 0;
  const ratingDisplay = hasRating ? ratingValue.toFixed(1) : null;
  const logoUrl = buildMakerLogoUrl(item.logoBlobPath);

  return (
    <Link
      href={`/katalog/${item.slug}`}
      className="group block h-full rounded-xl focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/40"
    >
      <article className="panel card-lift flex h-full flex-col overflow-hidden rounded-xl border border-zinc-800">
        {/* Decorative: the <h3> below already names the maker. */}
        <div
          aria-hidden
          className="flex aspect-video w-full items-center justify-center border-b border-zinc-800/80 bg-surface-elevated p-5"
        >
          {logoUrl ? (
            <Image
              src={logoUrl}
              alt=""
              width={LOGO_WIDTH}
              height={LOGO_HEIGHT}
              sizes="(max-width: 640px) 100vw, (max-width: 1280px) 50vw, 320px"
              className="h-full w-full object-contain"
            />
          ) : (
            <Avatar name={item.companyName} size="xl" />
          )}
        </div>

        <div className="flex flex-1 flex-col gap-2 p-4">
          <div className="flex flex-wrap items-center gap-2">
            <h3 className="min-w-0 text-base font-semibold text-zinc-100 group-hover:text-white">
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

          <p className="flex items-center gap-1.5 text-sm text-zinc-500">
            <Icon name="mapPin" size={14} />
            <span className="truncate">{item.city}</span>
          </p>

          {item.bio && (
            <p className="line-clamp-2 text-sm leading-relaxed text-zinc-400">{item.bio}</p>
          )}

          <div className="mt-auto flex flex-wrap items-center justify-between gap-x-3 gap-y-1.5 border-t border-zinc-800/80 pt-3">
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
          </div>
        </div>
      </article>
    </Link>
  );
}
