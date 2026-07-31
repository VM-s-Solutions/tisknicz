import Image from 'next/image';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { buildProductImageUrl, type ProductDetail } from '@/lib/api-client-helpers/catalog';
import { t } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';

const IMAGE_WIDTH = 96;
const IMAGE_HEIGHT = 72;

/**
 * Sticky order-summary card for the checkout form (T-0084a, Q1 lock —
 * single page + sticky summary). Server Component: renders purely from
 * the public `ProductDetail` DTO. The shipping price is NOT computed
 * client-side (no business logic) — an i18n note explains that shipping
 * is itemised after submission; the authoritative total comes back in
 * `CreateOrderResponse` and is displayed on /objednavka/[id].
 */
export function OrderSummary({ product }: { readonly product: ProductDetail }) {
  const imageUrl = buildProductImageUrl(product.images[0]?.blobPath);

  return (
    <Card variant="accent" padding="md" className="flex flex-col gap-4 lg:sticky lg:top-24">
      <h2 className="text-xs font-semibold tracking-widest text-zinc-500 uppercase">
        {t('checkout.summary.product')}
      </h2>

      <div className="flex items-start gap-4">
        <div className="relative h-18 w-24 shrink-0 overflow-hidden rounded-xl bg-zinc-900">
          {imageUrl ? (
            <Image
              src={imageUrl}
              alt={t('catalog.product.image_alt', { title: product.title })}
              width={IMAGE_WIDTH}
              height={IMAGE_HEIGHT}
              sizes="96px"
              className="h-full w-full object-cover"
            />
          ) : (
            <div className="flex h-full w-full items-center justify-center text-zinc-500">
              <Icon name="image" size={20} />
            </div>
          )}
        </div>
        <div className="flex min-w-0 flex-col gap-1">
          <p className="truncate text-sm font-semibold text-zinc-100">{product.title}</p>
          <p className="flex flex-wrap items-center gap-2 text-xs text-zinc-400">
            <span>{product.makerCompanyName}</span>
            {product.makerIsVerified ? (
              <Badge variant="brand" dot={false}>
                {t('catalog.maker.verified')}
              </Badge>
            ) : null}
          </p>
        </div>
      </div>

      <p className="text-2xl font-bold text-brand-400">
        <SummaryPrice product={product} />
      </p>

      <div aria-hidden="true" className="divider-glow" />

      <div className="flex flex-col gap-2 text-xs leading-relaxed text-zinc-500">
        <p>{t('checkout.summary.shippingNote')}</p>
        <p>{t('checkout.summary.totalNote')}</p>
      </div>
    </Card>
  );
}

/**
 * Price line. `OnRequest` products never reach this page (the entry
 * guard redirects to the product detail), but the non-CZK guard at the
 * card boundary stays per patterns.md B.10.
 */
function SummaryPrice({ product }: { readonly product: ProductDetail }) {
  if (product.priceType === 'OnRequest' || product.priceCurrency !== 'CZK') {
    return <>{t('catalog.product.price.on_request')}</>;
  }
  const formatted = formatCzk(product.priceAmountMinor, product.priceCurrency);
  if (product.priceType === 'From') {
    return <>{t('catalog.product.price.from', { price: formatted })}</>;
  }
  return <>{formatted}</>;
}
