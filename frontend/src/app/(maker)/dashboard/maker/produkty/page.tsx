import Link from 'next/link';
import type { Metadata } from 'next';
import { PageHeader } from '@/components/shared/page-header';
import { Alert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-state';
import { Icon } from '@/components/ui/icon';
import {
  getMyProducts,
  MAKER_PRODUCTS_DEFAULT_PAGE_SIZE,
  MAKER_PRODUCTS_MAX_PAGE_SIZE,
  type MakerProductsPage,
} from '@/lib/api-client-helpers/maker-products';
import { t } from '@/lib/i18n';
import { Pagination } from './pagination';
import { MakerProductCard } from './product-card';

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.maker.products.metadata.title'),
    description: t('dashboard.maker.products.metadata.description'),
  };
}

// Always render fresh — the dashboard reflects mutations the maker just
// made (create / update / delete / image upload), so SSG / ISR would
// stale out immediately.
export const dynamic = 'force-dynamic';

interface PageProps {
  readonly searchParams: Promise<Record<string, string | string[] | undefined>>;
}

function readString(value: string | string[] | undefined): string {
  if (Array.isArray(value)) return value[0] ?? '';
  return value ?? '';
}

function parsePositiveInt(raw: string, fallback: number, max: number = Number.MAX_SAFE_INTEGER): number {
  const parsed = Number.parseInt(raw, 10);
  if (!Number.isFinite(parsed) || parsed < 1) return fallback;
  return Math.min(parsed, max);
}

/**
 * Display filter over the fetched page (T-0174, audit MAKER-L6b): soft-
 * deleted products used to clutter the grid forever with no way to hide
 * them. The backend read has no is-active parameter yet, so this filters
 * the CURRENT page's items — counts and pagination stay those of the
 * unfiltered set (noted in the ticket; a backend param is the follow-up).
 */
type ActivityFilter = 'all' | 'active' | 'inactive';

function parseFilter(raw: string): ActivityFilter {
  return raw === 'active' || raw === 'inactive' ? raw : 'all';
}

export default async function MakerProductsPage({ searchParams }: PageProps) {
  const sp = await searchParams;
  const page = parsePositiveInt(readString(sp.page), 1);
  // Honor a URL-provided pageSize (clamped to the backend's
  // MaxPageSize) so deep-linking, share-links and pagination URLs all
  // round-trip cleanly. T-0049 Copilot review M2.
  const pageSize = parsePositiveInt(
    readString(sp.pageSize),
    MAKER_PRODUCTS_DEFAULT_PAGE_SIZE,
    MAKER_PRODUCTS_MAX_PAGE_SIZE,
  );
  const filter = parseFilter(readString(sp.filter));

  const result = await getMyProducts({ page, pageSize });

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <div className="mb-8">
          <PageHeader
            title={t('dashboard.maker.products.title')}
            subtitle={t('dashboard.maker.products.subtitle')}
            actions={
              <Link
                href="/dashboard/maker/produkty/novy"
                className="inline-flex items-center gap-2 rounded-lg border border-brand-500/60 px-5 py-2.5 text-sm font-semibold text-brand-300 transition-colors duration-150 hover:border-brand-400 hover:bg-tint-brand hover:text-brand-200 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400 focus-visible:ring-offset-2 focus-visible:ring-offset-surface-primary"
              >
                <Icon name="plus" size={16} />
                {t('dashboard.maker.products.cta.create')}
              </Link>
            }
          />
        </div>

        {result.success ? (
          <MakerProductsResults data={result.value} filter={filter} />
        ) : (
          <MakerProductsError />
        )}
      </div>
    </section>
  );
}

function MakerProductsResults({
  data,
  filter,
}: {
  readonly data: MakerProductsPage;
  readonly filter: ActivityFilter;
}) {
  if (data.items.length === 0) {
    return <MakerProductsEmpty />;
  }

  // PagedData<T>'s `totalPages` / `hasNextPage` / `hasPreviousPage` are
  // optional on the wire (the generated `IPagedDataOfMakerProductListItem`
  // marks them as such). They're always populated by the backend's
  // PagedData constructor (T-0049a verified) but TypeScript can't know
  // that, so we provide narrow fallbacks here.
  const totalPages = data.totalPages ?? 1;
  const hasNext = data.hasNextPage ?? false;
  const hasPrevious = data.hasPreviousPage ?? false;

  const visibleItems =
    filter === 'all' ? data.items : data.items.filter((item) => item.isActive === (filter === 'active'));

  return (
    <>
      <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-zinc-500">
          {t('dashboard.maker.products.count', { count: data.totalCount })}
        </p>
        <ActivityFilterChips active={filter} pageSize={data.pageSize} />
      </div>
      {visibleItems.length === 0 ? (
        <p className="py-8 text-center text-sm text-zinc-500">
          {t('dashboard.maker.products.filter.empty')}
        </p>
      ) : (
        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {visibleItems.map((item) => (
            <MakerProductCard key={item.productId} item={item} />
          ))}
        </div>
      )}
      <Pagination
        page={data.page}
        totalPages={totalPages}
        hasNext={hasNext}
        hasPrevious={hasPrevious}
        pageSize={data.pageSize}
        defaultPageSize={MAKER_PRODUCTS_DEFAULT_PAGE_SIZE}
        filter={filter === 'all' ? undefined : filter}
      />
    </>
  );
}

function ActivityFilterChips({
  active,
  pageSize,
}: {
  readonly active: ActivityFilter;
  readonly pageSize: number;
}) {
  const options: readonly { value: ActivityFilter; labelKey: Parameters<typeof t>[0] }[] = [
    { value: 'all', labelKey: 'dashboard.maker.products.filter.all' },
    { value: 'active', labelKey: 'dashboard.maker.products.filter.active' },
    { value: 'inactive', labelKey: 'dashboard.maker.products.filter.inactive' },
  ];
  return (
    <nav aria-label={t('dashboard.maker.products.filter.label')} className="flex items-center gap-2">
      {options.map((option) => {
        const sp = new URLSearchParams();
        if (option.value !== 'all') sp.set('filter', option.value);
        if (pageSize !== MAKER_PRODUCTS_DEFAULT_PAGE_SIZE) sp.set('pageSize', String(pageSize));
        const query = sp.toString();
        const isActive = option.value === active;
        return (
          <Link
            key={option.value}
            href={query ? `/dashboard/maker/produkty?${query}` : '/dashboard/maker/produkty'}
            aria-current={isActive ? 'page' : undefined}
            className={`rounded-lg border px-3 py-1.5 text-xs font-semibold transition-colors ${
              isActive
                ? 'border-brand-500/60 text-brand-300'
                : 'border-zinc-700 text-zinc-400 hover:border-brand-500/60 hover:text-brand-300'
            }`}
          >
            {t(option.labelKey)}
          </Link>
        );
      })}
    </nav>
  );
}

function MakerProductsEmpty() {
  return (
    <EmptyState
      icon="package"
      title={t('dashboard.maker.products.empty.title')}
      description={t('dashboard.maker.products.empty.description')}
      action={
        <Link
          href="/dashboard/maker/produkty/novy"
          className="inline-flex items-center gap-2 rounded-lg border border-brand-500/60 px-5 py-2.5 text-sm font-semibold text-brand-300 transition-colors duration-150 hover:border-brand-400 hover:bg-tint-brand hover:text-brand-200"
        >
          <Icon name="plus" size={16} />
          {t('dashboard.maker.products.empty.cta')}
        </Link>
      }
    />
  );
}

function MakerProductsError() {
  return (
    <Alert variant="error">
      <div className="flex flex-col gap-3">
        <div>
          <p className="font-semibold">{t('dashboard.maker.products.error.title')}</p>
          <p className="mt-1 text-sm opacity-90">{t('dashboard.maker.products.error.body')}</p>
        </div>
        <Link
          href="/dashboard/maker/produkty"
          className="inline-flex w-fit items-center gap-2 rounded-lg border border-error/40 px-4 py-2 text-sm font-semibold text-error transition-colors hover:bg-tint-error-strong hover:text-on-tint-error"
        >
          {t('dashboard.maker.products.error.retry')}
        </Link>
      </div>
    </Alert>
  );
}
