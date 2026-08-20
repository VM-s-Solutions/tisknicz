import Link from 'next/link';
import { notFound } from 'next/navigation';
import type { Metadata } from 'next';
import { Alert } from '@/components/ui/alert';
import { Avatar } from '@/components/ui/avatar';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { Stars } from '@/components/ui/stars';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';
import {
  buildMakerLogoUrl,
  getProductById,
  RATING_BP_PER_STAR,
  type ProductDetail,
} from '@/lib/api-client-helpers/catalog';
import { formatWeight } from '@/lib/format/weight';
import { formatCzk } from '@/lib/money/formatter';
import { getDisplaySession } from '@/lib/auth/display-session';
import { canonicalUrl } from '@/lib/seo/site-url';
import { truncateForMeta } from '@/lib/seo/truncate-for-meta';
import { ProductGallery } from './product-gallery';

interface PageProps {
  readonly params: Promise<{ productId: string }>;
}

export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const { productId } = await params;
  // Canonical stays the requested URL even on a NotFound (T-0131 AC-9).
  const url = canonicalUrl(`/produkt/${productId}`);
  const result = await getProductById(productId);
  if (!result.success) {
    // Only branch the title on NotFound — a transient backend error
    // shouldn't tell a search-engine indexer that the product doesn't
    // exist (T-0047 code-quality review nit #1; carried forward).
    const title =
      result.error.type === 'NotFound'
        ? `${t('catalog.product_detail.not_found.title')} — ${t('catalog.maker.metadata.title_suffix')}`
        : t('catalog.maker.metadata.title_suffix');
    const description = t('catalog.product_detail.metadata.fallback_description');
    return {
      title,
      description,
      alternates: { canonical: url },
      // Next 16's typed `OpenGraph.type` union does not include the OG
      // `product` value (it models a subset of the OG protocol). We emit
      // the framework-supported `website` type — a valid, clean OG card.
      // Upgrading to `og:type=product` requires either a raw `<meta>`
      // passthrough (which would duplicate the og:type tag) or a Next
      // type widening; deferred. (T-0131 deviation flag.)
      openGraph: { title, description, url, type: 'website' },
      twitter: { card: 'summary', title, description },
    };
  }
  const product = result.value;
  const description = product.description?.trim()
    ? truncateForMeta(product.description, 160)
    : t('catalog.product_detail.metadata.fallback_description');
  const title = `${product.title} — ${product.makerCompanyName} — ${t('catalog.maker.metadata.title_suffix')}`;
  return {
    title,
    description,
    alternates: { canonical: url },
    // Next 16's typed `OpenGraph.type` union has no OG `product` value;
    // the framework-supported `website` type ships a clean card (see the
    // NotFound branch above for the deviation rationale).
    openGraph: { title, description, url, type: 'website' },
    twitter: { card: 'summary', title, description },
  };
}

export default async function ProductDetailPage({ params }: PageProps) {
  const { productId } = await params;
  // The (public) layout already reads the cookie store for the
  // session-aware navbar, so this costs no extra round trip.
  const [result, session] = await Promise.all([getProductById(productId), getDisplaySession()]);

  if (!result.success) {
    if (result.error.type === 'NotFound') {
      notFound();
    }
    return (
      <section className="mx-auto flex max-w-5xl flex-col gap-6 px-4 py-10 sm:px-6 lg:px-8">
        <Link
          href="/katalog"
          className="inline-flex items-center gap-1.5 self-start text-sm text-zinc-400 transition-colors hover:text-zinc-200"
        >
          <Icon name="chevronLeft" size={16} />
          {t('catalog.maker.back_to_catalog')}
        </Link>
        <Alert variant="error">
          <p className="font-semibold">{t('catalog.product_detail.error.title')}</p>
          <p className="mt-1">{resolveErrorMessage(result.error)}</p>
        </Alert>
      </section>
    );
  }

  const product = result.value;
  const description = product.description?.trim();

  return (
    <section className="mx-auto flex max-w-5xl flex-col gap-6 px-4 py-10 sm:px-6 lg:px-8">
      <Link
        href="/katalog"
        className="inline-flex items-center gap-1.5 self-start text-sm text-zinc-400 transition-colors hover:text-zinc-200"
      >
        <Icon name="chevronLeft" size={16} />
        {t('catalog.maker.back_to_catalog')}
      </Link>
      <div className="flex flex-col gap-8 lg:grid lg:grid-cols-[minmax(0,1fr)_24rem] lg:gap-8">
        <ProductGallery images={product.images} title={product.title} />
        <ProductInfo product={product} isMaker={session?.audience === 'maker'} />
      </div>

      {description ? (
        <Card padding="md" className="flex flex-col gap-4">
          <h2 className="text-xl font-semibold text-white">
            {t('catalog.product_detail.description.heading')}
          </h2>
          <div aria-hidden="true" className="divider-glow" />
          <p className="whitespace-pre-line break-words text-base text-zinc-300">{description}</p>
        </Card>
      ) : null}
    </section>
  );
}

/**
 * Right-column info block. Server Component — the only state on this
 * page lives in <see cref="ProductGallery"/>. Renders title, price,
 * by-maker link (with verified badge), weight, and the order CTA.
 *
 * `isMaker` swaps the CTA for a note: an account is bound to one
 * audience (`User.MatchesAudience`), so a maker following the CTA hit a
 * login screen their own credentials could never satisfy.
 */
export function ProductInfo({
  product,
  isMaker = false,
}: {
  readonly product: ProductDetail;
  readonly isMaker?: boolean;
}) {
  return (
    <Card variant="accent" padding="md" className="flex h-fit flex-col gap-5">
      <div className="flex flex-col gap-3">
        <h1 className="text-shine text-3xl font-bold tracking-tight sm:text-4xl">
          {product.title}
        </h1>
        <p className="flex items-center gap-2 text-sm text-zinc-400">
          <Stars value={product.ratingAverageBp / RATING_BP_PER_STAR} size={15} />
          {product.ratingCount > 0 ? (
            <span>
              {t('catalog.product_detail.rating', {
                rating: (product.ratingAverageBp / RATING_BP_PER_STAR).toFixed(1),
                count: product.ratingCount,
              })}
            </span>
          ) : (
            <span>{t('catalog.product_detail.rating_none')}</span>
          )}
        </p>
        <div className="flex flex-wrap items-center gap-3">
          <p className="text-3xl font-semibold text-brand-400">
            <ProductPrice product={product} />
          </p>
          <Badge variant={product.fulfillmentType === 'InStock' ? 'success' : 'info'}>
            {t(`product.fulfillmentType.${product.fulfillmentType}`)}
          </Badge>
        </div>
      </div>

      <div aria-hidden="true" className="divider-glow" />

      <div className="flex flex-col gap-3">
        <Link
          href={`/katalog/${encodeURIComponent(product.makerSlug)}`}
          className="inline-flex flex-wrap items-center gap-2 rounded-md text-sm text-zinc-300 transition-colors hover:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400 focus-visible:ring-offset-2 focus-visible:ring-offset-surface-primary"
        >
          {/* Decorative: the adjacent "by {maker}" text names them. */}
          <Avatar
            src={buildMakerLogoUrl(product.makerLogoBlobPath)}
            name={product.makerCompanyName}
            size="xs"
          />
          <span>
            {t('catalog.product_detail.heading.by_maker', { maker: product.makerCompanyName })}
          </span>
          {product.makerIsVerified ? (
            <Badge variant="brand" dot={false}>
              {t('catalog.maker.verified')}
            </Badge>
          ) : null}
        </Link>

        {product.makerPersonalPickupEnabled ? (
          <div className="flex flex-col gap-1">
            <p className="inline-flex items-center gap-2 text-sm font-medium text-zinc-200">
              <Icon name="mapPin" size={15} className="text-brand-400" />
              {t('catalog.maker.pickup.heading')}
            </p>
            {product.makerPickupNote?.trim() ? (
              <p className="whitespace-pre-line break-words pl-6 text-sm text-zinc-400">
                {product.makerPickupNote.trim()}
              </p>
            ) : null}
          </div>
        ) : null}

        <p className="text-sm text-zinc-400">
          {t('catalog.product_detail.weight', { value: formatWeight(product.weightGrams) })}
        </p>
      </div>

      <div className="pt-2">
        {isMaker ? (
          <p className="text-sm text-zinc-400">{t('catalog.product_detail.cta.maker_note')}</p>
        ) : (
          <Link
            href={`/objednavka?productId=${encodeURIComponent(product.productId)}`}
            className="inline-flex items-center gap-2 rounded-lg border border-brand-500/60 px-5 py-2.5 text-sm font-semibold text-brand-300 transition-colors hover:border-brand-400 hover:bg-brand-500/10 hover:text-brand-200 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400 focus-visible:ring-offset-2 focus-visible:ring-offset-surface-primary"
          >
            {t('catalog.product_detail.cta.order')}
            <Icon name="arrowRight" size={16} />
          </Link>
        )}
      </div>
    </Card>
  );
}

/**
 * Inline product-price renderer. Mirrors the variant routing in T-0047's
 * <c>ProductCard.ProductPrice</c>: <c>Fixed</c> shows the formatted
 * amount, <c>From</c> wraps it in "od {price}", <c>OnRequest</c> + any
 * non-CZK currency routes to the <c>on_request</c> copy at the card
 * boundary (never calling <see cref="formatCzk"/> with non-CZK).
 */
function ProductPrice({ product }: { readonly product: ProductDetail }) {
  if (product.priceType === 'OnRequest' || product.priceCurrency !== 'CZK') {
    return <>{t('catalog.product.price.on_request')}</>;
  }
  const formatted = formatCzk(product.priceAmountMinor, product.priceCurrency);
  if (product.priceType === 'From') {
    return <>{t('catalog.product.price.from', { price: formatted })}</>;
  }
  return <>{formatted}</>;
}
