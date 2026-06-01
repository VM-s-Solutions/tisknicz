import Link from 'next/link';
import { notFound } from 'next/navigation';
import type { Metadata } from 'next';
import { Alert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';
import { getProductById, type ProductDetail } from '@/lib/api-client-helpers/catalog';
import { formatCzk } from '@/lib/money/formatter';
import { truncateForMeta } from '@/lib/seo/truncate-for-meta';
import { ProductGallery } from './product-gallery';

interface PageProps {
  readonly params: Promise<{ productId: string }>;
}

/**
 * Format a product weight for display (T-0048 AC-6). Czech locale:
 * <c>&lt;1000</c> g shows the integer with " g"; <c>&gt;=1000</c> g
 * shows kg to one decimal with a Czech comma decimal separator
 * (<c>Intl.NumberFormat('cs-CZ')</c> handles both the comma and the
 * thousands NBSP if we ever hit four-digit kg values).
 */
function formatWeight(grams: number): string {
  if (grams < 1000) {
    return `${grams} g`;
  }
  const kg = grams / 1000;
  const formatted = new Intl.NumberFormat('cs-CZ', {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  }).format(kg);
  return `${formatted} kg`;
}

export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const { productId } = await params;
  const result = await getProductById(productId);
  if (!result.success) {
    // Only branch the title on NotFound — a transient backend error
    // shouldn't tell a search-engine indexer that the product doesn't
    // exist (T-0047 code-quality review nit #1; carried forward).
    const title =
      result.error.type === 'NotFound'
        ? `${t('catalog.product_detail.not_found.title')} — ${t('catalog.maker.metadata.title_suffix')}`
        : t('catalog.maker.metadata.title_suffix');
    return {
      title,
      description: t('catalog.product_detail.metadata.fallback_description'),
    };
  }
  const product = result.value;
  const description = product.description?.trim()
    ? truncateForMeta(product.description, 160)
    : t('catalog.product_detail.metadata.fallback_description');
  return {
    title: `${product.title} — ${product.makerCompanyName} — ${t('catalog.maker.metadata.title_suffix')}`,
    description,
  };
}

export default async function ProductDetailPage({ params }: PageProps) {
  const { productId } = await params;
  const result = await getProductById(productId);

  if (!result.success) {
    if (result.error.type === 'NotFound') {
      notFound();
    }
    return (
      <section className="mx-auto flex max-w-5xl flex-col gap-6 px-4 py-10 sm:px-6 lg:px-8">
        <Alert variant="error">
          <p className="font-semibold">{t('catalog.product_detail.error.title')}</p>
          <p className="mt-1">{t('catalog.product_detail.error.body')}</p>
        </Alert>
        <div>
          <Link
            href="/katalog"
            className="inline-flex items-center gap-2 text-sm font-medium text-zinc-400 transition-colors hover:text-white"
          >
            <Icon name="arrowLeft" size={16} />
            {t('catalog.maker.back_to_catalog')}
          </Link>
        </div>
      </section>
    );
  }

  const product = result.value;
  const description = product.description?.trim();

  return (
    <section className="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-10 sm:px-6 lg:px-8">
      <div className="flex flex-col gap-8 lg:grid lg:grid-cols-[minmax(0,1fr)_24rem] lg:gap-8">
        <ProductGallery images={product.images} title={product.title} />
        <ProductInfo product={product} />
      </div>

      {description ? (
        <div className="flex flex-col gap-3">
          <h2 className="text-xl font-semibold text-white">
            {t('catalog.product_detail.description.heading')}
          </h2>
          <p className="whitespace-pre-line text-base text-zinc-300">{description}</p>
        </div>
      ) : null}

      <div className="pt-2">
        <Link
          href="/katalog"
          className="inline-flex items-center gap-2 text-sm font-medium text-zinc-400 transition-colors hover:text-white"
        >
          <Icon name="arrowLeft" size={16} />
          {t('catalog.maker.back_to_catalog')}
        </Link>
      </div>
    </section>
  );
}

/**
 * Right-column info block. Server Component — the only state on this
 * page lives in <see cref="ProductGallery"/>. Renders title, price,
 * by-maker link (with verified badge), weight, and the order CTA.
 */
function ProductInfo({ product }: { readonly product: ProductDetail }) {
  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-col gap-3">
        <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
          {product.title}
        </h1>
        <p className="text-2xl font-semibold text-brand-400">
          <ProductPrice product={product} />
        </p>
      </div>

      <Link
        href={`/katalog/${encodeURIComponent(product.makerSlug)}`}
        className="inline-flex flex-wrap items-center gap-2 text-sm text-zinc-300 transition-colors hover:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400 focus-visible:ring-offset-2 focus-visible:ring-offset-surface-primary rounded"
      >
        <span>
          {t('catalog.product_detail.heading.by_maker', { maker: product.makerCompanyName })}
        </span>
        {product.makerIsVerified ? (
          <Badge variant="brand">
            <Icon name="verified" size={14} />
            {t('catalog.maker.verified')}
          </Badge>
        ) : null}
      </Link>

      <p className="text-sm text-zinc-400">
        {t('catalog.product_detail.weight', { value: formatWeight(product.weightGrams) })}
      </p>

      <div className="pt-2">
        <Link
          href={`/objednavka?productId=${encodeURIComponent(product.productId)}`}
          className="inline-flex w-full items-center justify-center gap-2 rounded-xl bg-brand-400 px-6 py-3 text-base font-semibold text-zinc-950 transition-all duration-200 hover:bg-brand-300 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400 focus-visible:ring-offset-2 focus-visible:ring-offset-surface-primary sm:w-auto"
        >
          {t('catalog.product_detail.cta.order')}
          <Icon name="arrowRight" size={16} />
        </Link>
      </div>
    </div>
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
