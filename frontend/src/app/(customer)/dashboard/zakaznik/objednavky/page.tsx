import Link from 'next/link';
import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { PageHeader } from '@/components/shared/page-header';
import { Alert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-state';
import { Icon } from '@/components/ui/icon';
import {
  type CustomerOrdersInput,
  type CustomerOrdersPage,
  getCustomerOrders,
  OrderSort,
  OrderState,
} from '@/lib/api-client-helpers/orders-client';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';
import type { ApiError } from '@/lib/runtime/result';
import { OrdersFilters } from './filters-client';
import { OrderRows } from './order-row';
import { Pagination } from './pagination';

/**
 * Customer dashboard order list (T-0086a, US-customer-0016). Server
 * Component: filters/sort/page live in URL searchParams (Q5 lock — no
 * client store), the SSR list fetch forwards the customer audience
 * cookie (patterns.md B.14 / ADR 0024), and pagination is `<Link>`-based
 * per B.8. Invalid `state`/`sort` values are display-side canonicalised
 * to the default (ignored, not errored) — the backend Validator stays
 * authoritative for everything that reaches it (page clamps, inverted
 * date ranges).
 */

export function generateMetadata(): Metadata {
  return {
    title: t('customer.orders.metadata.title'),
  };
}

// Always render fresh — the list reflects payments/messages that just
// happened, so SSG / ISR would stale out immediately.
export const dynamic = 'force-dynamic';

const ROUTE_PATH = '/dashboard/zakaznik/objednavky';

const ORDER_STATE_VALUES = new Set<string>(Object.values(OrderState));
const ORDER_SORT_VALUES = new Set<string>(Object.values(OrderSort));
/** Display-side shape gate for `DatePicker` (`yyyy-MM-dd`) round-trips — backend binder is the authority. */
const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

interface PageProps {
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

export default async function CustomerOrdersPage({ searchParams }: PageProps) {
  const sp = await searchParams;

  const page = parsePositiveInt(readString(sp.page), 1);
  const rawState = readString(sp.state);
  const rawSort = readString(sp.sort);
  const rawDateFrom = readString(sp.dateFrom);
  const rawDateTo = readString(sp.dateTo);

  // Canonicalise: unknown enum values / malformed dates degrade to the
  // default instead of a 400 page (T-0086a technical note).
  const state = ORDER_STATE_VALUES.has(rawState) ? (rawState as OrderState) : undefined;
  const sort =
    ORDER_SORT_VALUES.has(rawSort) && rawSort !== OrderSort.CreatedAtDesc
      ? (rawSort as OrderSort)
      : undefined;
  const dateFrom = ISO_DATE_PATTERN.test(rawDateFrom) ? rawDateFrom : undefined;
  const dateTo = ISO_DATE_PATTERN.test(rawDateTo) ? rawDateTo : undefined;

  const input: CustomerOrdersInput = { page, state, dateFrom, dateTo, sort };
  const result = await getCustomerOrders(input);

  if (!result.success && result.error.type === 'Unauthorized') {
    // The login page serves at /login — the (auth) route group adds no
    // URL segment (T-0084b precedent). AC-11: no partial dashboard render.
    redirect(`/login?redirect=${encodeURIComponent(ROUTE_PATH)}`);
  }

  // Preserved params for pagination links (everything except `page`) —
  // only non-default values are emitted (patterns.md B.8).
  const baseParams: Record<string, string> = {};
  if (state) baseParams.state = state;
  if (dateFrom) baseParams.dateFrom = dateFrom;
  if (dateTo) baseParams.dateTo = dateTo;
  if (sort) baseParams.sort = sort;

  const hasActiveFilters =
    state !== undefined || dateFrom !== undefined || dateTo !== undefined;

  /** The URL the user is actually on — so a retry re-runs THIS request. */
  const currentHref = (params: Record<string, string>, currentPage: number): string => {
    const sp = new URLSearchParams(params);
    if (currentPage > 1) sp.set('page', String(currentPage));
    const query = sp.toString();
    return query ? `${ROUTE_PATH}?${query}` : ROUTE_PATH;
  };

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <div className="mb-8">
          <PageHeader
            title={t('customer.orders.title')}
            subtitle={t('customer.orders.subtitle')}
          />
        </div>

        <OrdersFilters
          initialState={state ?? ''}
          initialDateFrom={dateFrom ?? ''}
          initialDateTo={dateTo ?? ''}
          initialSort={sort ?? ''}
        />

        <div className="mt-8">
          {result.success ? (
            <OrdersResults
              data={result.value}
              baseParams={baseParams}
              hasActiveFilters={hasActiveFilters}
            />
          ) : (
            <OrdersError error={result.error} retryHref={currentHref(baseParams, page)} />
          )}
        </div>
      </div>
    </section>
  );
}

interface OrdersResultsProps {
  readonly data: CustomerOrdersPage;
  readonly baseParams: Readonly<Record<string, string>>;
  readonly hasActiveFilters: boolean;
}

function OrdersResults({ data, baseParams, hasActiveFilters }: OrdersResultsProps) {
  if (data.totalCount === 0) {
    return hasActiveFilters ? <OrdersNoMatch /> : <OrdersEmpty />;
  }

  // PagedData<T>'s `totalPages` / `hasNextPage` / `hasPreviousPage` are
  // optional on the wire (T-0049 precedent) — narrow fallbacks here.
  const totalPages = data.totalPages ?? 1;
  const hasNext = data.hasNextPage ?? false;
  const hasPrevious = data.hasPreviousPage ?? false;

  return (
    <>
      {/* GitHub box — the count lives in the container's header row. */}
      <OrderRows items={data.items} totalCount={data.totalCount} />
      <Pagination
        page={data.page}
        totalPages={totalPages}
        hasNext={hasNext}
        hasPrevious={hasPrevious}
        baseParams={baseParams}
      />
    </>
  );
}

function OrdersEmpty() {
  return (
    <EmptyState
      icon="shoppingBag"
      title={t('customer.orders.empty.title')}
      description={t('customer.orders.empty.description')}
      action={
        <Link
          href="/katalog"
          className="inline-flex items-center gap-2 rounded-lg border border-brand-500/60 px-5 py-2.5 text-sm font-semibold text-brand-300 transition-colors duration-150 hover:border-brand-400 hover:bg-brand-500/10 hover:text-brand-200"
        >
          {t('customer.orders.empty.cta')}
          <Icon name="arrowRight" size={16} />
        </Link>
      }
    />
  );
}

function OrdersNoMatch() {
  return (
    <EmptyState
      icon="search"
      title={t('customer.orders.noMatch.title')}
      description={t('customer.orders.noMatch.description')}
      action={
        <Link
          href={ROUTE_PATH}
          className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-5 py-2.5 text-sm font-medium text-zinc-200 transition-colors duration-150 hover:border-brand-500/60 hover:text-brand-300"
        >
          {t('customer.orders.noMatch.clear')}
        </Link>
      }
    />
  );
}

function OrdersError({
  error,
  retryHref,
}: {
  readonly error: ApiError;
  /** Current URL — T-0173 (audit CUST-L2 / MAKER-L2): the retry used to
   * link the bare route, silently discarding the tab, filters and page
   * the user was on. A Validation failure (e.g. an inverted date range)
   * is the one case where clearing IS the fix, so it says so instead. */
  readonly retryHref: string;
}) {
  const isValidation = error.type === 'Validation';
  // AC-5 (review NEW-3): Czech copy mapped from the error code — a 400
  // (e.g. inverted date range) must not read as a server outage.
  return (
    <Alert variant="error">
      <div className="flex flex-col gap-3">
        <div>
          <p className="font-semibold">{t('customer.orders.error.title')}</p>
          <p className="mt-1 text-sm opacity-90">{resolveErrorMessage(error)}</p>
        </div>
        <Link
          href={isValidation ? ROUTE_PATH : retryHref}
          className="inline-flex w-fit items-center gap-2 rounded-lg border border-red-500/30 px-4 py-2 text-sm font-semibold text-red-300 transition-colors duration-150 hover:border-red-400/60 hover:text-red-200"
        >
          {isValidation ? t('dashboard.orders.retry_clear_filters') : t('customer.orders.error.retry')}
        </Link>
      </div>
    </Alert>
  );
}
