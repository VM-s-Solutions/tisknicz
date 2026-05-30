import Link from 'next/link';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import type { MakerListItem } from '@/lib/api-client-helpers/catalog';
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
  // RatingAverageBp is 0..50000 basis points → 0.0-5.0 stars.
  const ratingValue = hasRating ? (item.ratingAverageBp / 1000) : 0;
  const ratingDisplay = hasRating ? ratingValue.toFixed(1) : null;

  return (
    <Link href={`/katalog/${item.slug}`} className="group block focus:outline-none">
      <Card
        padding="md"
        hover
        className="flex h-full flex-col gap-4 focus-visible:ring-2 focus-visible:ring-brand-400/40"
      >
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0 flex-1">
            <h3 className="truncate text-base font-semibold text-zinc-100 group-hover:text-brand-400">
              {item.companyName}
            </h3>
            <p className="mt-1 flex items-center gap-1.5 text-sm text-zinc-500">
              <Icon name="mapPin" size={14} />
              <span className="truncate">{item.city}</span>
            </p>
          </div>
          {item.isVerified && (
            <Badge variant="brand" className="shrink-0">
              <Icon name="verified" size={12} />
              {t('catalog.card.verified')}
            </Badge>
          )}
        </div>

        {item.bio && (
          <p className="line-clamp-2 text-sm leading-relaxed text-zinc-400">
            {item.bio}
          </p>
        )}

        <div className="mt-auto flex items-center justify-between gap-3 border-t border-zinc-800 pt-4">
          <div className="flex items-center gap-1.5 text-sm">
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
                <Icon name="starOutline" size={14} className="text-zinc-600" />
                <span className="text-zinc-500">{t('catalog.card.rating_none')}</span>
              </>
            )}
          </div>
          <p className="flex items-center gap-1.5 text-xs text-zinc-500">
            <Icon name="shoppingBag" size={12} />
            {t('catalog.card.orders', { count: item.totalOrders })}
          </p>
        </div>
      </Card>
    </Link>
  );
}
