import Link from 'next/link';
import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Icon } from '@/components/ui/icon';
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

function parsePositiveInt(raw: string, fallback: number, max: number = Number.MAX_SAFE_INTEGER): number {
  const parsed = Number.parseInt(raw, 10);
  if (!Number.isFinite(parsed) || parsed < 1) return fallback;
  return Math.min(parsed, max);
}

export default async function MakerOrdersPage({ searchParams }: PageProps) {
  const sp = await searchParams;

  const tab = parseOrderListTab(readString(sp.tab));
  const page = parsePositiveInt(readString(sp.page), 1);
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

  return (
    <section className="bg-surface-primary py-12 lg:py-16">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <header className="mb-8">
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t('dashboard.maker.orders.title')}
          </h1>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.maker.orders.subtitle')}
          </p>
        </header>

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
            <OrdersError error={result.error} />
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

  return (
    <>
      <p className="mb-6 text-sm text-zinc-500">
        {t('dashboard.maker.orders.count', { count: data.totalCount })}
      </p>
      <OrderRows items={data.items} />
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
    <div className="flex flex-col items-center justify-center gap-4 rounded-2xl border border-dashed border-zinc-800 bg-surface-card px-6 py-20 text-center">
      <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-zinc-800 text-zinc-500">
        <Icon name={copy.icon} size={28} />
      </div>
      <div>
        <h2 className="text-lg font-semibold text-zinc-100">{t(copy.titleKey)}</h2>
        <p className="mt-2 max-w-md text-sm text-zinc-400">{t(copy.descriptionKey)}</p>
      </div>
    </div>
  );
}

function OrdersError({ error }: { readonly error: ApiError }) {
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
          href={ROUTE_PATH}
          className="inline-flex w-fit items-center gap-2 rounded-xl border border-red-800/50 px-4 py-2 text-sm font-semibold text-red-300 transition-colors hover:bg-red-950"
        >
          {t('dashboard.maker.orders.error.retry')}
        </Link>
      </div>
    </Alert>
  );
}
