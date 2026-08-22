import { cache } from 'react';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import type { Metadata } from 'next';
import {
  NavigationTransitionProvider,
  TransitionDim,
} from '@/components/shared/navigation-transition';
import { Alert } from '@/components/ui/alert';
import { Avatar } from '@/components/ui/avatar';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-state';
import { Icon } from '@/components/ui/icon';
import { Tooltip } from '@/components/ui/tooltip';
import { Stars } from '@/components/ui/stars';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';
import {
  buildMakerLogoUrl,
  getMakerBySlug,
  RATING_BP_PER_STAR,
  type MakerProductItem,
  type MakerProfile,
} from '@/lib/api-client-helpers/catalog';
import { canonicalUrl } from '@/lib/seo/site-url';
import { truncateForMeta } from '@/lib/seo/truncate-for-meta';
import { ProductCard } from './product-card';
import { ProductFilters } from './product-filters-client';
import { ReviewsSection } from './reviews-section';
/**
 * Per-request memo: `generateMetadata` and the page body both need the
 * same read, and `apiFetch` composes a fresh `AbortSignal.timeout` per
 * call — Next's fetch memoization opts OUT whenever an `init.signal` is
 * present (`next/dist/server/lib/dedupe-fetch.js`), so without `cache()`
 * every view issues two identical backend GETs. Scope is one server
 * request; `router.refresh()` is a new request and re-fetches.
 */
const loadMaker = cache(getMakerBySlug);


interface PageProps {
  readonly params: Promise<{ slug: string }>;
  readonly searchParams: Promise<Record<string, string | string[] | undefined>>;
}

export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const { slug } = await params;
  // Canonical stays the requested URL even on a NotFound — a 404 page is
  // not indexed, but the canonical is kept consistent (T-0131 C/AC-9).
  const url = canonicalUrl(`/katalog/${slug}`);
  const result = await loadMaker(slug);
  if (!result.success) {
    // Only branch the title on NotFound — a transient backend error
    // shouldn't tell a search-engine indexer that the maker doesn't
    // exist (T-0047 code-quality review nit #1).
    const title =
      result.error.type === 'NotFound'
        ? `${t('catalog.maker.not_found.title')} — ${t('catalog.maker.metadata.title_suffix')}`
        : t('catalog.maker.metadata.title_suffix');
    const description = t('catalog.maker.metadata.fallback_description');
    return {
      title,
      description,
      alternates: { canonical: url },
      openGraph: { title, description, url, type: 'profile' },
      twitter: { card: 'summary', title, description },
    };
  }
  const profile = result.value;
  const description = profile.bio?.trim()
    ? truncateForMeta(profile.bio, 160)
    : t('catalog.maker.metadata.fallback_description');
  const title = `${profile.companyName} — ${t('catalog.maker.metadata.title_suffix')}`;
  return {
    title,
    description,
    alternates: { canonical: url },
    openGraph: { title, description, url, type: 'profile' },
    twitter: { card: 'summary', title, description },
  };
}

function readString(value: string | string[] | undefined): string {
  if (Array.isArray(value)) return value[0] ?? '';
  return value ?? '';
}

/** Whole-Kč query value → minor units (haléře); null for absent/invalid. */
function parsePriceKcToMinor(raw: string): number | null {
  if (!raw.trim()) return null;
  const parsed = Number.parseInt(raw, 10);
  if (!Number.isFinite(parsed) || parsed < 0) return null;
  return parsed * 100;
}

interface ProductGridFilter {
  readonly minPriceMinor: number | null;
  readonly maxPriceMinor: number | null;
  readonly minRatingBp: number | null;
}

/**
 * Display-side narrowing of the already-fetched product list (the
 * profile endpoint returns ALL active products — no paging, so no
 * second round-trip is warranted). Price bounds apply only to products
 * with a real price; "Na poptávku" items are hidden while a price
 * bound is active because their price is unknown. The rating bound
 * requires at least one rating — an unrated product can't demonstrate
 * the requested minimum.
 */
function filterProducts(
  products: readonly MakerProductItem[],
  filter: ProductGridFilter,
): readonly MakerProductItem[] {
  const priceBound = filter.minPriceMinor !== null || filter.maxPriceMinor !== null;
  return products.filter((p) => {
    if (filter.minRatingBp !== null) {
      if (p.ratingCount === 0 || p.ratingAverageBp < filter.minRatingBp) return false;
    }
    if (priceBound) {
      if (p.priceType === 'OnRequest') return false;
      if (filter.minPriceMinor !== null && p.priceAmountMinor < filter.minPriceMinor) return false;
      if (filter.maxPriceMinor !== null && p.priceAmountMinor > filter.maxPriceMinor) return false;
    }
    return true;
  });
}

export default async function MakerProfilePage({ params, searchParams }: PageProps) {
  const { slug } = await params;
  const sp = await searchParams;
  const result = await loadMaker(slug);

  if (!result.success) {
    if (result.error.type === 'NotFound') {
      notFound();
    }
    return (
      <section className="mx-auto flex max-w-6xl flex-col gap-6 px-4 py-10 sm:px-6 lg:px-8">
        <Link
          href="/katalog"
          className="inline-flex items-center gap-1.5 self-start text-sm text-zinc-400 transition-colors hover:text-zinc-200"
        >
          <Icon name="chevronLeft" size={16} />
          {t('catalog.maker.back_to_catalog')}
        </Link>
        <Alert variant="error">
          <p className="font-semibold">{t('catalog.maker.error.title')}</p>
          <p className="mt-1">{resolveErrorMessage(result.error)}</p>
        </Alert>
      </section>
    );
  }

  const profile = result.value;

  const rawMinPrice = readString(sp.minPrice);
  const rawMaxPrice = readString(sp.maxPrice);
  const rawMinRating = readString(sp.minRating);
  const minRatingParsed = Number.parseInt(rawMinRating, 10);
  const minRatingStars =
    Number.isFinite(minRatingParsed) && minRatingParsed >= 1 && minRatingParsed <= 5
      ? minRatingParsed
      : null;

  const filter: ProductGridFilter = {
    minPriceMinor: parsePriceKcToMinor(rawMinPrice),
    maxPriceMinor: parsePriceKcToMinor(rawMaxPrice),
    minRatingBp: minRatingStars === null ? null : minRatingStars * RATING_BP_PER_STAR,
  };
  const filteredProducts = filterProducts(profile.products, filter);
  const isFiltered =
    filter.minPriceMinor !== null || filter.maxPriceMinor !== null || filter.minRatingBp !== null;

  return (
    <section className="mx-auto flex max-w-6xl flex-col gap-6 px-4 py-10 sm:px-6 lg:px-8">
      <Link
        href="/katalog"
        className="inline-flex items-center gap-1.5 self-start text-sm text-zinc-400 transition-colors hover:text-zinc-200"
      >
        <Icon name="chevronLeft" size={16} />
        {t('catalog.maker.back_to_catalog')}
      </Link>
      <NavigationTransitionProvider>
        <div className="flex flex-col gap-6 lg:grid lg:grid-cols-[19rem_minmax(0,1fr)] lg:items-start lg:gap-8">
          <aside className="flex flex-col gap-6 lg:sticky lg:top-24">
            <SellerPanel profile={profile} />
            <Card padding="md">
              {/* Keyed off canonical filter state so back/forward never
                  leaves stale inputs (T-0170, PUB-H1 — same treatment as
                  the catalog panel). */}
              <ProductFilters
                key={`${rawMinPrice}|${rawMaxPrice}|${minRatingStars ?? ''}`}
                initialMinPrice={rawMinPrice}
                initialMaxPrice={rawMaxPrice}
                initialMinRating={minRatingStars === null ? '' : String(minRatingStars)}
              />
            </Card>
          </aside>

          <div className="flex min-w-0 flex-col gap-8">
            <TransitionDim>
              <ProductsGrid
                products={filteredProducts}
                totalCount={profile.products.length}
                isFiltered={isFiltered}
              />
            </TransitionDim>
            <ReviewsSection reviews={profile.reviews} />
          </div>
        </div>
      </NavigationTransitionProvider>
    </section>
  );
}

/**
 * Left-column seller identity panel. Replaces the former full-width
 * header + the standalone pickup card: personal pickup is one row of
 * the seller panel now, not its own section, so the whole "who am I
 * buying from" story lives in a single sticky column next to the grid.
 */
function SellerPanel({ profile }: { readonly profile: MakerProfile }) {
  const ratingDisplay = (profile.ratingAverageBp / RATING_BP_PER_STAR).toFixed(1);
  const pickupNote = profile.pickupNote?.trim();
  const bio = profile.bio?.trim();

  return (
    <Card variant="accent" padding="lg" className="flex flex-col gap-5">
      <div className="flex items-center gap-4">
        {/* Decorative: the <h1> beside it already names the maker. */}
        <Avatar src={buildMakerLogoUrl(profile.logoBlobPath)} name={profile.companyName} size="lg" />
        <div className="min-w-0">
          <h1 className="text-shine break-words text-2xl font-bold tracking-tight">
            {profile.companyName}
          </h1>
          <p className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-zinc-400">
            <span className="inline-flex items-center gap-1">
              <Icon name="mapPin" size={14} />
              {profile.city}
            </span>
            {profile.legalForm ? <span>{profile.legalForm}</span> : null}
          </p>
        </div>
      </div>

      {profile.isVerified ? (
        <div className="flex flex-wrap items-center gap-2">
          <Tooltip content={t('catalog.card.verified_tooltip')}>
            <Badge variant="brand" dot={false}>
              {t('catalog.maker.verified')}
            </Badge>
          </Tooltip>
        </div>
      ) : null}

      <div className="flex flex-col gap-2 text-sm text-zinc-400">
        <span className="inline-flex items-center gap-2">
          <Stars value={profile.ratingAverageBp / RATING_BP_PER_STAR} />
          {profile.ratingCount > 0 ? (
            <span>
              {t('catalog.maker.stats.rating', {
                rating: ratingDisplay,
                count: profile.ratingCount,
              })}
            </span>
          ) : (
            <span>{t('catalog.maker.stats.rating_none')}</span>
          )}
        </span>
        <span>{t('catalog.maker.stats.orders', { count: profile.totalOrders })}</span>
      </div>

      {profile.personalPickupEnabled ? (
        <>
          <div aria-hidden="true" className="divider-glow" />
          <div className="flex flex-col gap-1">
            <p className="inline-flex items-center gap-2 text-sm font-semibold text-white">
              <Icon name="mapPin" size={15} className="text-brand-400" />
              {t('catalog.maker.pickup.heading')}
            </p>
            {pickupNote ? (
              <p className="whitespace-pre-line break-words text-sm text-zinc-400">{pickupNote}</p>
            ) : null}
          </div>
        </>
      ) : null}

      {bio ? (
        <>
          <div aria-hidden="true" className="divider-glow" />
          <p className="whitespace-pre-line break-words text-sm text-zinc-300">{bio}</p>
        </>
      ) : null}
    </Card>
  );
}

function ProductsGrid({
  products,
  totalCount,
  isFiltered,
}: {
  readonly products: readonly MakerProductItem[];
  readonly totalCount: number;
  readonly isFiltered: boolean;
}) {
  return (
    <section className="rounded-xl border border-zinc-800 bg-surface-card">
      <div className="flex items-center justify-between gap-3 rounded-t-xl border-b border-zinc-800 bg-surface-secondary/60 px-4 py-3">
        <h2 className="text-sm font-semibold text-zinc-100">
          {t('catalog.maker.products.heading')}
        </h2>
        {isFiltered ? (
          <span className="text-xs text-zinc-500">
            {t('catalog.maker.products.filtered_count', {
              shown: products.length,
              total: totalCount,
            })}
          </span>
        ) : totalCount > 0 ? (
          <span className="rounded-md bg-zinc-800 px-1.5 py-0.5 text-xs font-medium text-zinc-400">
            {totalCount}
          </span>
        ) : null}
      </div>
      <div className="p-4">
        {totalCount === 0 ? (
          <EmptyState icon="grid" title={t('catalog.maker.products.empty')} />
        ) : products.length === 0 ? (
          <EmptyState
            icon="filter"
            title={t('catalog.maker.products.empty_filtered.title')}
            description={t('catalog.maker.products.empty_filtered.description')}
          />
        ) : (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {products.map((item) => (
              <ProductCard key={item.productId} item={item} />
            ))}
          </div>
        )}
      </div>
    </section>
  );
}
