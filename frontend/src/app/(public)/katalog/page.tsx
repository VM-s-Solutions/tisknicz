import Link from 'next/link';
import type { Metadata } from 'next';
import { Alert } from '@/components/ui/alert';
import { Icon } from '@/components/ui/icon';
import {
  CATALOG_DEFAULT_PAGE_SIZE,
  type CatalogFilterInput,
  getPagedMakers,
  type MakerListItem,
} from '@/lib/api-client-helpers/catalog';
import { CATALOG_CATEGORIES } from '@/lib/catalog/categories';
import { t } from '@/lib/i18n';
import { canonicalUrl } from '@/lib/seo/site-url';
import { CatalogFilters } from './filters-client';
import { MakerCard } from './maker-card';
import { Pagination } from './pagination';

export function generateMetadata(): Metadata {
  const title = t('catalog.title');
  const description = t('catalog.subtitle');
  // Canonical is the UNFILTERED /katalog — filter query params are not
  // part of the canonical, so every filtered view consolidates onto the
  // one indexable catalog URL (duplicate-content hygiene, A.6/C.6).
  const url = canonicalUrl('/katalog');
  return {
    title,
    description,
    alternates: { canonical: url },
    openGraph: { title, description, url, type: 'website' },
    twitter: { card: 'summary', title, description },
  };
}

// Always render fresh — catalog data is server-side filtered and the
// filters live in the query string. No SSG / no ISR cache.
export const dynamic = 'force-dynamic';

const VALID_CATEGORY_SLUGS = new Set(CATALOG_CATEGORIES.map((c) => c.slug));

interface CatalogPageProps {
  readonly searchParams: Promise<Record<string, string | string[] | undefined>>;
}

function readString(value: string | string[] | undefined): string {
  if (Array.isArray(value)) return value[0] ?? '';
  return value ?? '';
}

function parsePositiveInt(raw: string, fallback: number): number {
  const parsed = Number.parseInt(raw, 10);
  if (!Number.isFinite(parsed) || parsed < 1) return fallback;
  return parsed;
}

export default async function CatalogPage({ searchParams }: CatalogPageProps) {
  const sp = await searchParams;
  const rawCategory = readString(sp.category);
  const rawCity = readString(sp.city).trim();
  const rawMinRating = readString(sp.minRating);
  const rawPage = readString(sp.page);

  const category = VALID_CATEGORY_SLUGS.has(rawCategory) ? rawCategory : '';
  const minRatingStarsParsed = Number.parseInt(rawMinRating, 10);
  const minRatingStars =
    Number.isFinite(minRatingStarsParsed) && minRatingStarsParsed >= 1 && minRatingStarsParsed <= 5
      ? minRatingStarsParsed
      : undefined;
  const page = parsePositiveInt(rawPage, 1);

  const filter: CatalogFilterInput = {
    country: 'CZ',
    category: category || undefined,
    city: rawCity || undefined,
    minRatingStars,
    page,
    pageSize: CATALOG_DEFAULT_PAGE_SIZE,
  };

  const result = await getPagedMakers(filter);

  // Filters component reads URL state; canonicalised values keep its
  // initial render in sync with what the server actually used.
  const initialMinRating = minRatingStars !== undefined ? String(minRatingStars) : '';

  // Preserved params for pagination links (everything except `page`).
  const baseParams: Record<string, string> = {};
  if (category) baseParams.category = category;
  if (rawCity) baseParams.city = rawCity;
  if (initialMinRating) baseParams.minRating = initialMinRating;

  return (
    <section className="bg-surface-primary py-20 lg:py-24">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <header className="max-w-4xl">
          <h1 className="text-4xl font-bold tracking-tight text-white sm:text-5xl">
            {t('catalog.title')}
          </h1>
          <p className="mt-6 max-w-3xl text-lg leading-relaxed text-zinc-400">
            {t('catalog.subtitle')}
          </p>
        </header>

        <div className="mt-14">
          <CatalogFilters
            initialCategory={category}
            initialCity={rawCity}
            initialMinRating={initialMinRating}
          />
        </div>

        <div className="mt-10">
          {result.success ? (
            <CatalogResults
              items={result.value.items}
              page={result.value.page}
              totalPages={result.value.totalPages}
              hasNext={result.value.hasNext}
              hasPrevious={result.value.hasPrevious}
              totalCount={result.value.totalCount}
              baseParams={baseParams}
            />
          ) : (
            <CatalogError />
          )}
        </div>
      </div>
    </section>
  );
}

interface CatalogResultsProps {
  readonly items: readonly MakerListItem[];
  readonly page: number;
  readonly totalPages: number;
  readonly hasNext: boolean;
  readonly hasPrevious: boolean;
  readonly totalCount: number;
  readonly baseParams: Readonly<Record<string, string>>;
}

function CatalogResults({
  items,
  page,
  totalPages,
  hasNext,
  hasPrevious,
  totalCount,
  baseParams,
}: CatalogResultsProps) {
  if (items.length === 0) {
    return <CatalogEmpty />;
  }

  return (
    <>
      <p className="mb-5 text-sm text-zinc-500">
        {t('catalog.pagination.results', { count: totalCount })}
      </p>
      <ul className="divide-y divide-zinc-800 border-y border-zinc-800">
        {items.map((item) => (
          <li key={item.makerId}>
            <MakerCard item={item} />
          </li>
        ))}
      </ul>
      <Pagination
        page={page}
        totalPages={totalPages}
        hasNext={hasNext}
        hasPrevious={hasPrevious}
        baseParams={baseParams}
      />
    </>
  );
}

function CatalogEmpty() {
  return (
    <div className="flex flex-col items-center justify-center gap-4 border-y border-dashed border-zinc-800 px-6 py-16 text-center">
      <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-zinc-800 text-zinc-500">
        <Icon name="search" size={28} />
      </div>
      <div>
        <h2 className="text-lg font-semibold text-zinc-100">
          {t('catalog.empty.title')}
        </h2>
        <p className="mt-2 text-sm text-zinc-400">
          {t('catalog.empty.description')}
        </p>
      </div>
      <Link
        href="/katalog"
        className="inline-flex items-center gap-2 rounded-full border border-brand-500/60 px-5 py-2.5 text-sm font-medium tracking-wide text-brand-300 transition-all duration-200 hover:border-brand-400 hover:text-brand-200 hover:shadow-lg hover:shadow-brand-500/20"
      >
        {t('catalog.empty.reset')}
      </Link>
    </div>
  );
}

function CatalogError() {
  return (
    <Alert variant="error" className="border border-red-900/50 bg-red-950/20">
      <div className="flex flex-col gap-3">
        <div>
          <p className="font-semibold">{t('catalog.error.title')}</p>
          <p className="mt-1 text-sm opacity-90">{t('error.transient')}</p>
        </div>
        <Link
          href="/katalog"
          className="inline-flex w-fit items-center gap-2 rounded-full border border-red-500/50 px-4 py-2 text-sm font-medium text-red-300 transition-all duration-200 hover:border-red-400 hover:text-red-200"
        >
          {t('catalog.error.retry')}
        </Link>
      </div>
    </Alert>
  );
}
