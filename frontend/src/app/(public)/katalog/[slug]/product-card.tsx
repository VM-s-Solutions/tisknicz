import Image from 'next/image';
import Link from 'next/link';
import { Card } from '@/components/ui/card';
import { Stars } from '@/components/ui/stars';
import { t } from '@/lib/i18n';
import {
  buildProductImageUrl,
  RATING_BP_PER_STAR,
  type MakerProductItem,
} from '@/lib/api-client-helpers/catalog';
import { formatCzk } from '@/lib/money/formatter';

interface ProductCardProps {
  readonly item: MakerProductItem;
}

const IMAGE_WIDTH = 400;
const IMAGE_HEIGHT = 300;

/**
 * Card for a single product on the maker profile (T-0047). Server
 * Component; wraps the entire card in a <c>next/link</c> to the product
 * detail page (T-0048 ships the target route — link 404s gracefully
 * until then per T-0047 scope).
 *
 * Price formatting maps the backend's <c>PriceType</c> string union to
 * one of three display variants: fixed, "od {price}", or
 * "Na poptávku". All currency text goes through
 * <see cref="formatCzk"/> + i18n.
 */
export function ProductCard({ item }: ProductCardProps) {
  const imageUrl = buildProductImageUrl(item.primaryImageBlobPath);

  return (
    <Link
      href={`/produkt/${encodeURIComponent(item.productId)}`}
      className="group block h-full focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400 focus-visible:ring-offset-2 focus-visible:ring-offset-surface-primary rounded-2xl"
    >
      <Card padding="none" hover variant="elevated" className="flex h-full flex-col overflow-hidden">
        <div className="relative aspect-[4/3] w-full bg-surface-elevated">
          {imageUrl ? (
            <Image
              src={imageUrl}
              alt={t('catalog.product.image_alt', { title: item.title })}
              width={IMAGE_WIDTH}
              height={IMAGE_HEIGHT}
              sizes="(max-width: 768px) 100vw, (max-width: 1280px) 50vw, 400px"
              className="h-full w-full object-cover"
            />
          ) : (
            <div className="flex h-full w-full items-center justify-center text-sm text-zinc-500">
              {t('catalog.product.no_image')}
            </div>
          )}
        </div>
        <div className="flex flex-1 flex-col gap-2 p-4">
          <h3 className="text-base font-semibold text-white line-clamp-2">{item.title}</h3>
          <p className="flex items-center gap-1.5 text-xs text-zinc-400">
            {item.ratingCount > 0 ? (
              <>
                <Stars value={item.ratingAverageBp / RATING_BP_PER_STAR} size={13} />
                <span>
                  {(item.ratingAverageBp / RATING_BP_PER_STAR).toFixed(1)}{' '}
                  {t('catalog.card.rating_count', { count: item.ratingCount })}
                </span>
              </>
            ) : (
              <span>{t('catalog.card.rating_none')}</span>
            )}
          </p>
          <p className="mt-auto text-sm font-medium text-brand-400">
            <ProductPrice item={item} />
          </p>
        </div>
      </Card>
    </Link>
  );
}

function ProductPrice({ item }: ProductCardProps) {
  if (item.priceType === 'OnRequest' || item.priceCurrency !== 'CZK') {
    // Non-CZK shouldn't reach the frontend during MVP (the backend
    // snapshots prices in CountryConfiguration's CZK), but a single
    // contract-violating row must not 500 the whole profile route.
    // Fall back to the "ask the maker" copy and let the rest of the
    // grid render. formatCzk's own assertCzkCurrency would throw if a
    // non-CZK amount reached it, so this guard keeps the formatter
    // honest while letting the card boundary stay forgiving.
    return <>{t('catalog.product.price.on_request')}</>;
  }
  const formatted = formatCzk(item.priceAmountMinor, item.priceCurrency);
  if (item.priceType === 'From') {
    return <>{t('catalog.product.price.from', { price: formatted })}</>;
  }
  return <>{formatted}</>;
}
