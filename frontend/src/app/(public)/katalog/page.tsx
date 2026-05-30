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
import { CatalogFilters } from './filters-client';
import { MakerCard } from './maker-card';
import { Pagination } from './pagination';

export function generateMetadata(): Metadata {
  return {
    title: t('catalog.title'),
    description: t('catalog.subtitle'),
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
    <section className="bg-surface-primary py-16 lg:py-24">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <header className="mb-10">
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t('catalog.title')}
          </h1>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('catalog.subtitle')}
          </p>
        </header>

        <div className="grid grid-cols-1 gap-8 md:grid-cols-[18rem_minmax(0,1fr)]">
          <aside>
            <CatalogFilters
              initialCategory={category}
              initialCity={rawCity}
              initialMinRating={initialMinRating}
            />
          </aside>

          <div>
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
      <p className="mb-6 text-sm text-zinc-500">
        {t('catalog.pagination.results', { count: totalCount })}
      </p>
      <div className="grid grid-cols-1 gap-5 md:grid-cols-2 lg:grid-cols-3">
        {items.map((item) => (
          <MakerCard key={item.makerId} item={item} />
        ))}
      </div>
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
    <div className="flex flex-col items-center justify-center gap-4 rounded-2xl border border-dashed border-zinc-800 bg-surface-card px-6 py-20 text-center">
      <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-zinc-800 text-zinc-500">
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
        className="inline-flex items-center gap-2 rounded-xl border border-brand-400/50 px-5 py-2.5 text-sm font-semibold text-brand-400 transition-colors hover:bg-brand-400/10"
      >
        {t('catalog.empty.reset')}
      </Link>
    </div>
  );
}

function CatalogError() {
  return (
    <Alert variant="error">
      <div className="flex flex-col gap-3">
        <div>
          <p className="font-semibold">{t('catalog.error.title')}</p>
          <p className="mt-1 text-sm opacity-90">{t('error.transient')}</p>
        </div>
        <Link
          href="/katalog"
          className="inline-flex w-fit items-center gap-2 rounded-xl border border-red-800/50 px-4 py-2 text-sm font-semibold text-red-300 transition-colors hover:bg-red-950"
        >
          {t('catalog.error.retry')}
        </Link>
      </div>
    </Alert>
  );
}
