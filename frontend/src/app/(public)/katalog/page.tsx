import Link from 'next/link';
import type { Metadata } from 'next';
import {
  NavigationTransitionProvider,
  TransitionDim,
} from '@/components/shared/navigation-transition';
import { PageHeader } from '@/components/shared/page-header';
import { RefreshButton } from '@/components/shared/refresh-button';
import { ScrollToTop } from '@/components/shared/scroll-to-top';
import { Alert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-state';
import {
  CATALOG_DEFAULT_PAGE_SIZE,
  type CatalogFilterInput,
  getPagedMakers,
  type MakerLegalType,
  type MakerListItem,
} from '@/lib/api-client-helpers/catalog';
import { getCachedCatalogCategories } from '@/lib/catalog/category-cache';
import { CATALOG_CATEGORIES } from '@/lib/catalog/categories';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';
import type { ApiError } from '@/lib/runtime/result';
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
  const items = await getCachedCatalogCategories();
  if (items.length > 0) {
    return items.map((c) => ({ slug: c.slug, label: c.name }));
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

/**
 * Repeated query param → deduped list. `?category=a&category=b` arrives
 * as an array; a single `?category=a` as a string. Order is preserved so
 * the canonical URL is stable for a given click sequence.
 */
function readStringList(value: string | string[] | undefined): readonly string[] {
  const raw = Array.isArray(value) ? value : value === undefined ? [] : [value];
  return [...new Set(raw.map((v) => v.trim()).filter(Boolean))];
}

/**
 * Canonicalise `?legalType=` against the two values the backend accepts.
 * Anything else (a hand-typed or stale value) degrades to "no
 * constraint" rather than an empty result — same posture as an unknown
 * category slug.
 */
function readLegalType(value: string | string[] | undefined): MakerLegalType | undefined {
  const raw = readString(value);
  return raw === 'LegalEntity' || raw === 'NaturalPerson' ? raw : undefined;
}

function parsePositiveInt(raw: string, fallback: number): number {
  const parsed = Number.parseInt(raw, 10);
  if (!Number.isFinite(parsed) || parsed < 1) return fallback;
  return parsed;
}

export default async function CatalogPage({ searchParams }: CatalogPageProps) {
  const sp = await searchParams;
  const rawCategories = readStringList(sp.category);
  const rawCity = readString(sp.city).trim();
  const rawMinRating = readString(sp.minRating);
  const legalType = readLegalType(sp.legalType);
  const rawPage = readString(sp.page);

  const minRatingStarsParsed = Number.parseInt(rawMinRating, 10);
  const minRatingStars =
    Number.isFinite(minRatingStarsParsed) && minRatingStarsParsed >= 1 && minRatingStarsParsed <= 5
      ? minRatingStarsParsed
      : undefined;
  const page = parsePositiveInt(rawPage, 1);

  const buildFilter = (selected: readonly string[]): CatalogFilterInput => ({
    country: 'CZ',
    categories: selected.length > 0 ? selected : undefined,
    city: rawCity || undefined,
    minRatingStars,
    legalType,
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
  let selectedCategories: readonly string[];
  let result: Awaited<ReturnType<typeof getPagedMakers>>;
  if (rawCategories.length === 0) {
    selectedCategories = [];
    [categoryOptions, result] = await Promise.all([
      categoriesPromise,
      getPagedMakers(buildFilter([])),
    ]);
  } else {
    categoryOptions = await categoriesPromise;
    const validCategorySlugs = new Set(categoryOptions.map((c) => c.slug));
    selectedCategories = rawCategories.filter((slug) => validCategorySlugs.has(slug));
    result = await getPagedMakers(buildFilter(selectedCategories));
  }

  // Filters component reads URL state; canonicalised values keep its
  // initial render in sync with what the server actually used.
  const initialMinRating = minRatingStars !== undefined ? String(minRatingStars) : '';

  // Preserved params for pagination links (everything except `page`).
  // Built as URLSearchParams, not a record — `category` repeats.
  const baseParams = new URLSearchParams();
  for (const slug of selectedCategories) baseParams.append('category', slug);
  if (rawCity) baseParams.set('city', rawCity);
  if (initialMinRating) baseParams.set('minRating', initialMinRating);
  if (legalType) baseParams.set('legalType', legalType);
  const baseQuery = baseParams.toString();
  const droppedCategoryCount = rawCategories.length - selectedCategories.length;

  return (
    <section className="py-14 lg:py-18">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <PageHeader title={t('catalog.title')} subtitle={t('catalog.subtitle')} />

        <NavigationTransitionProvider>
          <div className="mt-10 flex flex-col gap-6 lg:grid lg:grid-cols-[17rem_minmax(0,1fr)] lg:items-start lg:gap-8">
            {/* Sticky only engages when the panel is SHORTER than the space
                below `top`; the old scrolling category column made the card
                taller than a laptop viewport, so it scrolled away instead.
                The panel is now fixed-height regardless of category count
                (the list lives in an overlay), so no cap is needed here —
                and an `overflow` on this element would clip that overlay. */}
            <aside className="lg:sticky lg:top-24">
              <Card variant="elevated" padding="sm" className="sm:p-5">
                {/* Keyed off the canonical filter state so back/forward and
                    reset/retry links can never leave stale controls behind
                    (T-0170, audit PUB-H1): a URL change remounts the panel
                    with fresh initial values. */}
                <CatalogFilters
                  key={baseQuery}
                  categories={categoryOptions}
                  initialCategories={selectedCategories}
                  initialCity={rawCity}
                  initialMinRating={initialMinRating}
                  initialLegalType={legalType}
                />
              </Card>
            </aside>

            <div className="min-w-0">
              {/* T-0171 (audit PUB-M5): an admin-renamed or deactivated
                  category silently produced the FULL unfiltered catalog with
                  nothing checked in the sidebar — the visitor had no way to
                  tell their link had gone stale. */}
              {droppedCategoryCount > 0 ? (
                <div className="mb-5">
                  <Alert variant="warning">{t('catalog.category_gone')}</Alert>
                </div>
              ) : null}
              {result.success ? (
                <CatalogResults
                  items={result.value.items}
                  page={result.value.page}
                  totalPages={result.value.totalPages}
                  hasNext={result.value.hasNextPage}
                  hasPrevious={result.value.hasPreviousPage}
                  totalCount={result.value.totalCount}
                  baseQuery={baseQuery}
                  hasActiveFilters={baseQuery !== ''}
                />
              ) : (
                <CatalogError error={result.error} />
              )}
            </div>
          </div>
        </NavigationTransitionProvider>
      </div>
      <ScrollToTop />
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
  readonly baseQuery: string;
  readonly hasActiveFilters: boolean;
}

/**
 * Which empty surface an empty item list actually is (T-0170, audit
 * PUB-H3): a stale `?page=99` deep link, a filter that matches nothing,
 * and a genuinely empty catalog are three different situations — the old
 * single "no makers match your filter + clear filters" copy misdiagnosed
 * two of them and dead-ended the out-of-range case entirely.
 */
export function catalogEmptyKind(input: {
  readonly itemCount: number;
  readonly totalCount: number;
  readonly hasActiveFilters: boolean;
}): 'none' | 'out_of_range' | 'filtered' | 'no_makers' {
  if (input.itemCount > 0) return 'none';
  if (input.totalCount > 0) return 'out_of_range';
  return input.hasActiveFilters ? 'filtered' : 'no_makers';
}

function CatalogResults({
  items,
  page,
  totalPages,
  hasNext,
  hasPrevious,
  totalCount,
  baseQuery,
  hasActiveFilters,
}: CatalogResultsProps) {
  const emptyKind = catalogEmptyKind({ itemCount: items.length, totalCount, hasActiveFilters });

  if (emptyKind === 'out_of_range') {
    return (
      <EmptyState
        icon="search"
        title={t('catalog.empty.out_of_range.title')}
        description={t('catalog.empty.out_of_range.description')}
        action={
          <Link
            href={baseQuery ? `/katalog?${baseQuery}` : '/katalog'}
            className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-5 py-2.5 text-sm font-semibold text-zinc-200 transition-colors duration-150 hover:border-brand-line hover:text-brand-300"
          >
            {t('catalog.empty.out_of_range.first_page')}
          </Link>
        }
      />
    );
  }

  if (emptyKind === 'filtered') {
    return (
      <EmptyState
        icon="search"
        title={t('catalog.empty.title')}
        description={t('catalog.empty.description')}
        action={
          <Link
            href="/katalog"
            className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-5 py-2.5 text-sm font-semibold text-zinc-200 transition-colors duration-150 hover:border-brand-line hover:text-brand-300"
          >
            {t('catalog.empty.reset')}
          </Link>
        }
      />
    );
  }

  if (emptyKind === 'no_makers') {
    return (
      <EmptyState
        icon="package"
        title={t('catalog.empty.no_makers.title')}
        description={t('catalog.empty.no_makers.description')}
        action={
          <Link
            href="/pro-makery"
            className="inline-flex items-center gap-2 rounded-lg border border-brand-line px-5 py-2.5 text-sm font-semibold text-brand-ink transition-colors duration-150 hover:border-brand-500 hover:bg-brand-fill-soft"
          >
            {t('catalog.empty.no_makers.cta')}
          </Link>
        }
      />
    );
  }

  return (
    <>
      <TransitionDim>
        <p className="mb-5 text-sm text-zinc-500">
          {t('catalog.pagination.results', { count: totalCount })}
        </p>
        <ul className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3 xl:gap-5">
          {items.map((item) => (
            <li key={item.makerId} className="min-w-0">
              <MakerCard item={item} />
            </li>
          ))}
        </ul>
      </TransitionDim>
      <Pagination
        page={page}
        totalPages={totalPages}
        hasNext={hasNext}
        hasPrevious={hasPrevious}
        baseQuery={baseQuery}
      />
    </>
  );
}

/**
 * The catalog's failure surface used to print `error.transient`
 * ("Server je momentálně nedostupný") for EVERY failure, whatever
 * actually happened. When the Public host's rate limiter answered 429,
 * the page still returned HTTP 200 and told the visitor the server was
 * down — a 200 in the network tab and a "server unavailable" banner on
 * screen, which is unfalsifiable from the outside and cost real
 * debugging time. Render what actually failed instead.
 */
function CatalogError({ error }: { readonly error: ApiError }) {
  return (
    <Alert variant="error">
      <div className="flex flex-col gap-3">
        <div>
          <p className="font-semibold">{t('catalog.error.title')}</p>
          <p className="mt-1 text-sm">{resolveErrorMessage(error)}</p>
        </div>
        {/* router.refresh() on the CURRENT URL — the old <Link href="/katalog">
            silently dropped the user's filters/page and self-links can no-op
            in the client router (T-0170, audit PUB-M3). */}
        <RefreshButton label={t('catalog.error.retry')} />
      </div>
    </Alert>
  );
}
