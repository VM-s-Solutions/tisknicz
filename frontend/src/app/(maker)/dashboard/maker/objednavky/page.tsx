import Link from 'next/link';
import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { PageHeader } from '@/components/shared/page-header';
import { Alert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-state';
import {
  getMakerOrders,
  MAKER_ORDERS_DEFAULT_PAGE_SIZE,
  MAKER_ORDERS_MAX_PAGE_SIZE,
  type MakerOrdersInput,
  type MakerOrdersPage,
  OrderSort,
  OrderState,
} from '@/lib/api-client-helpers/maker-orders';
import { t } from '@/lib/i18n';
import type { MessageKey } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';
import type { ApiError } from '@/lib/runtime/result';
import { OrdersFilters } from './filters-client';
import { OrderRows } from './order-row';
import {
  DEFAULT_ORDER_LIST_TAB,
  type OrderListTab,
  OrderTabs,
  parseOrderListTab,
} from './order-tabs';
import { Pagination } from './pagination';

/**
 * Maker dashboard order list (T-0087a, US-maker-0005). Server
 * Component: tab/filters/sort/page live in URL searchParams (Q5 lock —
 * no client store), the SSR list fetch forwards the maker audience
 * cookie (patterns.md B.14 / ADR 0024), and pagination + tabs are
 * `<Link>`-based per B.8. Each tab maps to AT MOST ONE `state` value —
 * exactly one list request per render, no client-side multi-state
 * merging (A.1 lock honoring T-0081 §A.3). The default tab is Nové
 * (Paid, awaiting acceptance) — the maker's first paint IS the
 * needs-action queue (US-maker-0005 AC-3 nudge). Invalid `sort`/date
 * values are display-side canonicalised to the default (ignored, not
 * errored) — the backend Validator stays authoritative.
 */

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.maker.orders.metadata.title'),
    description: t('dashboard.maker.orders.metadata.description'),
  };
}

// Always render fresh — the list reflects payments/messages/transitions
// that just happened, so SSG / ISR would stale out immediately.
export const dynamic = 'force-dynamic';

const ROUTE_PATH = '/dashboard/maker/objednavky';

const ORDER_SORT_VALUES = new Set<string>(Object.values(OrderSort));
/** Display-side shape gate for `<input type="date">` round-trips — backend binder is the authority. */
const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

/**
 * Tab → single-state mapping (A.1 lock) — presentation routing, not a
 * state machine. The ONE place to touch if product later widens "needs
 * action" semantics (T-0087a risk note).
 */
function tabToState(tab: OrderListTab): OrderState | undefined {
  switch (tab) {
    case 'nove':
      return OrderState.Paid;
    case 'vyroba':
      return OrderState.Accepted;
    case 'vse':
      return undefined;
  }
}

interface PageProps {
  readonly searchParams: Promise<Record<string, string | string[] | undefined>>;
}

function readString(value: string | string[] | undefined): string {
  if (Array.isArray(value)) return value[0] ?? '';
  return value ?? '';
}

/** Nothing deep-links past this; keeps an absurd page off the API. */
const MAX_DEEP_LINK_PAGE = 10_000;

function parsePositiveInt(raw: string, fallback: number, max: number = Number.MAX_SAFE_INTEGER): number {
  const parsed = Number.parseInt(raw, 10);
  if (!Number.isFinite(parsed) || parsed < 1) return fallback;
  return Math.min(parsed, max);
}

export default async function MakerOrdersPage({ searchParams }: PageProps) {
  const sp = await searchParams;

  const tab = parseOrderListTab(readString(sp.tab));
  // Bound the deep-linked page so a hand-typed ?page=9007199254740991
  // never reaches the backend (T-0173, audit MAKER-L5).
  const page = parsePositiveInt(readString(sp.page), 1, MAX_DEEP_LINK_PAGE);
  // Honor a URL-provided pageSize (clamped to the backend's MaxPageSize)
  // so deep links and pagination URLs round-trip cleanly (produkty
  // precedent, T-0049 review M2). UX-only clamp — backend authoritative.
  const pageSize = parsePositiveInt(
    readString(sp.pageSize),
    MAKER_ORDERS_DEFAULT_PAGE_SIZE,
    MAKER_ORDERS_MAX_PAGE_SIZE,
  );
  const rawSort = readString(sp.sort);
  const rawDateFrom = readString(sp.dateFrom);
  const rawDateTo = readString(sp.dateTo);

  // Canonicalise: unknown sort values / malformed dates degrade to the
  // default instead of a 400 page (T-0086a technical-note mirror).
  const sort =
    ORDER_SORT_VALUES.has(rawSort) && rawSort !== OrderSort.CreatedAtDesc
      ? (rawSort as OrderSort)
      : undefined;
  const dateFrom = ISO_DATE_PATTERN.test(rawDateFrom) ? rawDateFrom : undefined;
  const dateTo = ISO_DATE_PATTERN.test(rawDateTo) ? rawDateTo : undefined;

  // Exactly ONE list request per render, with at most one state (AC-2).
  const input: MakerOrdersInput = {
    page,
    pageSize,
    state: tabToState(tab),
    dateFrom,
    dateTo,
    sort,
  };
  const result = await getMakerOrders(input);

  if (!result.success && result.error.type === 'Unauthorized') {
    // The login page serves at /login — the (auth) route group adds no
    // URL segment (T-0086a precedent). No partial dashboard render.
    redirect(`/login?redirect=${encodeURIComponent(ROUTE_PATH)}`);
  }

  // Filter params preserved across tab switches (everything except
  // `page` and `tab` — order-tabs.tsx adds `tab` itself).
  const filterParams: Record<string, string> = {};
  if (dateFrom) filterParams.dateFrom = dateFrom;
  if (dateTo) filterParams.dateTo = dateTo;
  if (sort) filterParams.sort = sort;
  if (pageSize !== MAKER_ORDERS_DEFAULT_PAGE_SIZE) filterParams.pageSize = String(pageSize);

  // Pagination additionally preserves the active tab (non-default only).
  const paginationParams: Record<string, string> = { ...filterParams };
  if (tab !== DEFAULT_ORDER_LIST_TAB) paginationParams.tab = tab;

  /** The URL the maker is actually on — a retry must re-run THIS request,
   * tab and filters included (T-0173, audit MAKER-L2). */
  const currentHref = (() => {
    const sp = new URLSearchParams(paginationParams);
    if (page > 1) sp.set('page', String(page));
    const query = sp.toString();
    return query ? `${ROUTE_PATH}?${query}` : ROUTE_PATH;
  })();

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <div className="mb-8">
          <PageHeader
            title={t('dashboard.maker.orders.title')}
            subtitle={t('dashboard.maker.orders.subtitle')}
          />
        </div>

        <OrderTabs activeTab={tab} baseParams={filterParams} />

        <div className="mt-6">
          <OrdersFilters
            tabParam={tab === DEFAULT_ORDER_LIST_TAB ? '' : tab}
            pageSizeParam={pageSize !== MAKER_ORDERS_DEFAULT_PAGE_SIZE ? String(pageSize) : ''}
            initialDateFrom={dateFrom ?? ''}
            initialDateTo={dateTo ?? ''}
            initialSort={sort ?? ''}
          />
        </div>

        <div className="mt-8">
          {result.success ? (
            <OrdersResults data={result.value} tab={tab} baseParams={paginationParams} />
          ) : (
            <OrdersError error={result.error} retryHref={currentHref} />
          )}
        </div>
      </div>
    </section>
  );
}

interface OrdersResultsProps {
  readonly data: MakerOrdersPage;
  readonly tab: OrderListTab;
  readonly baseParams: Readonly<Record<string, string>>;
}

function OrdersResults({ data, tab, baseParams }: OrdersResultsProps) {
  if (data.totalCount === 0) {
    return <OrdersEmpty tab={tab} />;
  }

  // PagedData<T>'s `totalPages` / `hasNextPage` / `hasPreviousPage` are
  // optional on the wire (T-0049 precedent) — narrow fallbacks here.
  const totalPages = data.totalPages ?? 1;
  const hasNext = data.hasNextPage ?? false;
  const hasPrevious = data.hasPreviousPage ?? false;

  // T-0173 (audit MAKER-L5): a page past the end returned zero items with a
  // NONZERO total, so the maker saw an empty box under a count claiming
  // orders exist. Offer the last real page instead of a dead end.
  if (data.items.length === 0 && data.page > totalPages) {
    const lastPageParams = new URLSearchParams(baseParams);
    if (totalPages > 1) lastPageParams.set('page', String(totalPages));
    const query = lastPageParams.toString();
    return (
      <EmptyState
        icon="search"
        title={t('dashboard.maker.orders.outOfRange.title')}
        description={t('dashboard.maker.orders.outOfRange.description')}
        action={
          <Link
            href={query ? `${ROUTE_PATH}?${query}` : ROUTE_PATH}
            className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-5 py-2.5 text-sm font-semibold text-zinc-200 transition-colors hover:border-brand-500/60 hover:text-brand-300"
          >
            {t('dashboard.maker.orders.outOfRange.lastPage')}
          </Link>
        }
      />
    );
  }

  return (
    <>
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

interface EmptyCopy {
  readonly icon: 'checkCircle' | 'package' | 'shoppingBag';
  readonly titleKey: MessageKey;
  readonly descriptionKey: MessageKey;
}

/**
 * Per-tab empty states (AC-8): Nové is informational/positive — an
 * empty acceptance queue is good news, not an error; Ve výrobě is
 * neutral; Vše is onboarding-flavoured.
 */
const EMPTY_COPY: Record<OrderListTab, EmptyCopy> = {
  nove: {
    icon: 'checkCircle',
    titleKey: 'dashboard.maker.orders.empty.nove.title',
    descriptionKey: 'dashboard.maker.orders.empty.nove.description',
  },
  vyroba: {
    icon: 'package',
    titleKey: 'dashboard.maker.orders.empty.vyroba.title',
    descriptionKey: 'dashboard.maker.orders.empty.vyroba.description',
  },
  vse: {
    icon: 'shoppingBag',
    titleKey: 'dashboard.maker.orders.empty.vse.title',
    descriptionKey: 'dashboard.maker.orders.empty.vse.description',
  },
};

function OrdersEmpty({ tab }: { readonly tab: OrderListTab }) {
  const copy = EMPTY_COPY[tab];
  return (
    <EmptyState icon={copy.icon} title={t(copy.titleKey)} description={t(copy.descriptionKey)} />
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
  // AC-10 polish (review NEW-3): Czech copy mapped from the error code —
  // a 400 (e.g. inverted date range) must not read as a server outage.
  return (
    <Alert variant="error">
      <div className="flex flex-col gap-3">
        <div>
          <p className="font-semibold">{t('dashboard.maker.orders.error.title')}</p>
          <p className="mt-1 text-sm opacity-90">{resolveErrorMessage(error)}</p>
        </div>
        <Link
          href={isValidation ? ROUTE_PATH : retryHref}
          className="inline-flex w-fit items-center gap-2 rounded-lg border border-red-800/50 px-4 py-2 text-sm font-semibold text-red-300 transition-colors hover:bg-red-950"
        >
          {isValidation ? t('dashboard.orders.retry_clear_filters') : t('dashboard.maker.orders.error.retry')}
        </Link>
      </div>
    </Alert>
  );
}
