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

  const result = await getMyProducts({ page, pageSize });

  return (
    <section className="bg-surface-primary py-12 lg:py-16">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <div className="mb-8">
          <PageHeader
            title={t('dashboard.maker.products.title')}
            subtitle={t('dashboard.maker.products.subtitle')}
            actions={
              <Link
                href="/dashboard/maker/produkty/novy"
                className="inline-flex items-center gap-2 rounded-xl bg-brand-400 px-5 py-2.5 text-sm font-semibold text-zinc-950 transition-all duration-200 hover:bg-brand-300 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400 focus-visible:ring-offset-2 focus-visible:ring-offset-surface-primary"
              >
                <Icon name="plus" size={16} />
                {t('dashboard.maker.products.cta.create')}
              </Link>
            }
          />
        </div>

        {result.success ? (
          <MakerProductsResults data={result.value} />
        ) : (
          <MakerProductsError />
        )}
      </div>
    </section>
  );
}

function MakerProductsResults({ data }: { readonly data: MakerProductsPage }) {
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

  return (
    <>
      <p className="mb-6 text-sm text-zinc-500">
        {t('dashboard.maker.products.count', { count: data.totalCount })}
      </p>
      <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
        {data.items.map((item) => (
          <MakerProductCard key={item.productId} item={item} />
        ))}
      </div>
      <Pagination
        page={data.page}
        totalPages={totalPages}
        hasNext={hasNext}
        hasPrevious={hasPrevious}
        pageSize={data.pageSize}
        defaultPageSize={MAKER_PRODUCTS_DEFAULT_PAGE_SIZE}
      />
    </>
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
          className="inline-flex items-center gap-2 rounded-xl bg-brand-400 px-5 py-2.5 text-sm font-semibold text-zinc-950 transition-all duration-200 hover:bg-brand-300"
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
          className="inline-flex w-fit items-center gap-2 rounded-xl border border-red-800/50 px-4 py-2 text-sm font-semibold text-red-300 transition-colors hover:bg-red-950"
        >
          {t('dashboard.maker.products.error.retry')}
        </Link>
      </div>
    </Alert>
  );
}
