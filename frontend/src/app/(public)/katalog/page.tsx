import Link from 'next/link';
import type { Metadata } from 'next';
import { PageHeader } from '@/components/shared/page-header';
import { Alert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-state';
import {
  CATALOG_DEFAULT_PAGE_SIZE,
  type CatalogFilterInput,
  getCatalogCategories,
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

/**
 * Category options for the filter dropdown (T-0119). Data-driven from
 * the anonymous categories endpoint so admin-created categories appear;
 * degrades to the static launch list when the read fails (the maker
 * list itself has its own error surface).
 */
async function loadCategoryOptions(): Promise<readonly { slug: string; label: string }[]> {
  const result = await getCatalogCategories();
  if (result.success && result.value.items.length > 0) {
    return result.value.items.map((c) => ({ slug: c.slug, label: c.name }));
  }
  return CATALOG_CATEGORIES.map((c) => ({ slug: c.slug, label: t(c.labelKey) }));
}

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

  const minRatingStarsParsed = Number.parseInt(rawMinRating, 10);
  const minRatingStars =
    Number.isFinite(minRatingStarsParsed) && minRatingStarsParsed >= 1 && minRatingStarsParsed <= 5
      ? minRatingStarsParsed
      : undefined;
  const page = parsePositiveInt(rawPage, 1);

  const buildFilter = (category: string): CatalogFilterInput => ({
    country: 'CZ',
    category: category || undefined,
    city: rawCity || undefined,
    minRatingStars,
    page,
    pageSize: CATALOG_DEFAULT_PAGE_SIZE,
  });

  // Perf (T-0158): the two backend reads used to run serially — two full
  // round trips before first byte. Without a `category` param (the hot
  // path) the makers query doesn't depend on the category list, so both
  // run concurrently; a category-filtered URL keeps the original order
  // because the slug must canonicalise against the fetched list first
  // (invalid slug → unfiltered, not empty).
  const categoriesPromise = loadCategoryOptions();
  let categoryOptions: Awaited<typeof categoriesPromise>;
  let category: string;
  let result: Awaited<ReturnType<typeof getPagedMakers>>;
  if (rawCategory === '') {
    category = '';
    [categoryOptions, result] = await Promise.all([
      categoriesPromise,
      getPagedMakers(buildFilter('')),
    ]);
  } else {
    categoryOptions = await categoriesPromise;
    const validCategorySlugs = new Set(categoryOptions.map((c) => c.slug));
    category = validCategorySlugs.has(rawCategory) ? rawCategory : '';
    result = await getPagedMakers(buildFilter(category));
  }

  // Filters component reads URL state; canonicalised values keep its
  // initial render in sync with what the server actually used.
  const initialMinRating = minRatingStars !== undefined ? String(minRatingStars) : '';

  // Preserved params for pagination links (everything except `page`).
  const baseParams: Record<string, string> = {};
  if (category) baseParams.category = category;
  if (rawCity) baseParams.city = rawCity;
  if (initialMinRating) baseParams.minRating = initialMinRating;

  return (
    <section className="py-14 lg:py-18">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <PageHeader title={t('catalog.title')} subtitle={t('catalog.subtitle')} />

        <div className="mt-10 flex flex-col gap-6 lg:grid lg:grid-cols-[17rem_minmax(0,1fr)] lg:items-start lg:gap-8">
          <aside className="lg:sticky lg:top-24">
            <Card variant="elevated" padding="sm" className="sm:p-5">
              <CatalogFilters
                categories={categoryOptions}
                initialCategory={category}
                initialCity={rawCity}
                initialMinRating={initialMinRating}
              />
            </Card>
          </aside>

          <div className="min-w-0">
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
      <p className="mb-5 text-sm text-zinc-500">
        {t('catalog.pagination.results', { count: totalCount })}
      </p>
      <ul className="grid grid-cols-1 gap-4 xl:grid-cols-2 xl:gap-5">
        {items.map((item) => (
          <li key={item.makerId} className="min-w-0">
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
    <EmptyState
      icon="search"
      title={t('catalog.empty.title')}
      description={t('catalog.empty.description')}
      action={
        <Link
          href="/katalog"
          className="inline-flex items-center gap-2 rounded-full border border-brand-500/60 px-5 py-2.5 text-sm font-medium tracking-wide text-brand-300 transition-all duration-200 hover:border-brand-400 hover:text-brand-200 hover:shadow-lg hover:shadow-brand-500/20"
        >
          {t('catalog.empty.reset')}
        </Link>
      }
    />
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
