import Link from 'next/link';
import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Icon } from '@/components/ui/icon';
import {
  ADMIN_LIST_DEFAULT_PAGE_SIZE,
  ADMIN_LIST_MAX_PAGE_SIZE,
  type AdminOrdersInput,
  type AdminOrdersPage,
  getAdminOrders,
  OrderState,
} from '@/lib/api-client-helpers/admin-client';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';
import type { ApiError } from '@/lib/runtime/result';
import { OrderFilters } from './order-filters';
import { OrderRows } from './order-row';
import { Pagination } from './pagination';

/**
 * Admin all-orders list (T-0118a, US-admin-0009 AC-1). Server Component:
 * filters/page live in URL searchParams (no client store), the SSR fetch
 * forwards the admin-audience cookie (patterns.md B.14 / ADR 0024), and
 * filters/pagination are native-GET / `<Link>`-based per B.8. Sorted
 * `CreatedAt DESC` server-side. Unknown `state` and junk `page` degrade
 * to defaults (the backend Validator stays authoritative).
 */

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.admin.orders.metadata.title'),
    description: t('dashboard.admin.orders.metadata.description'),
  };
}

export const dynamic = 'force-dynamic';

const ROUTE_PATH = '/dashboard/admin/orders';
const ORDER_STATE_VALUES = new Set<string>(Object.values(OrderState));

interface PageProps {
  readonly searchParams: Promise<Record<string, string | string[] | undefined>>;
}

function readString(value: string | string[] | undefined): string {
  if (Array.isArray(value)) return value[0] ?? '';
  return value ?? '';
}

function parsePositiveInt(raw: string, fallback: number, max: number): number {
  const parsed = Number.parseInt(raw, 10);
  if (!Number.isFinite(parsed) || parsed < 1) return fallback;
  return Math.min(parsed, max);
}

export default async function AdminOrdersPage({ searchParams }: PageProps) {
  const sp = await searchParams;

  const page = parsePositiveInt(readString(sp.page), 1, Number.MAX_SAFE_INTEGER);
  const pageSize = parsePositiveInt(
    readString(sp.pageSize),
    ADMIN_LIST_DEFAULT_PAGE_SIZE,
    ADMIN_LIST_MAX_PAGE_SIZE,
  );
  const rawState = readString(sp.state);
  const rawCountry = readString(sp.country).trim().toUpperCase();
  const rawMakerId = readString(sp.makerId).trim();
  const rawCustomerEmail = readString(sp.customerEmail).trim();

  // Canonicalise: an unknown state degrades to "unset" rather than a 400
  // page (maker-orders precedent). No date-range filter on the orders
  // list — the generated `adminOrders` contract carries no dateFrom/dateTo
  // param (T-0118a §C); the gap is logged as a backend follow-up.
  const state = ORDER_STATE_VALUES.has(rawState) ? (rawState as OrderState) : undefined;
  const country = rawCountry !== '' ? rawCountry : undefined;
  const makerId = rawMakerId !== '' ? rawMakerId : undefined;
  const customerEmail = rawCustomerEmail !== '' ? rawCustomerEmail : undefined;

  const input: AdminOrdersInput = { page, pageSize, state, country, makerId, customerEmail };
  const result = await getAdminOrders(input);

  if (!result.success && result.error.type === 'Unauthorized') {
    redirect(`/admin/login?redirect=${encodeURIComponent(ROUTE_PATH)}`);
  }

  // Filter params preserved across pages (everything except `page`).
  const paginationParams: Record<string, string> = {};
  if (state) paginationParams.state = state;
  if (country) paginationParams.country = country;
  if (makerId) paginationParams.makerId = makerId;
  if (customerEmail) paginationParams.customerEmail = customerEmail;
  if (pageSize !== ADMIN_LIST_DEFAULT_PAGE_SIZE) paginationParams.pageSize = String(pageSize);

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <header className="mb-8">
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t('dashboard.admin.orders.title')}
          </h1>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.admin.orders.subtitle')}
          </p>
        </header>

        <OrderFilters
          state={state ?? ''}
          country={country ?? ''}
          makerId={makerId ?? ''}
          customerEmail={customerEmail ?? ''}
        />

        <div className="mt-8">
          {result.success ? (
            <OrdersResults data={result.value} baseParams={paginationParams} />
          ) : (
            <OrdersError error={result.error} />
          )}
        </div>
      </div>
    </section>
  );
}

interface OrdersResultsProps {
  readonly data: AdminOrdersPage;
  readonly baseParams: Readonly<Record<string, string>>;
}

function OrdersResults({ data, baseParams }: OrdersResultsProps) {
  if (data.totalCount === 0) {
    return <OrdersEmpty />;
  }

  const totalPages = data.totalPages ?? 1;
  const hasNext = data.hasNextPage ?? false;
  const hasPrevious = data.hasPreviousPage ?? false;

  return (
    <>
      <p className="mb-6 text-sm text-zinc-500">
        {t('dashboard.admin.orders.count', { count: data.totalCount })}
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

function OrdersEmpty() {
  return (
    <div className="flex flex-col items-center justify-center gap-4 rounded-2xl border border-dashed border-zinc-800 bg-surface-card px-6 py-20 text-center">
      <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-zinc-800 text-zinc-500">
        <Icon name="shoppingBag" size={28} />
      </div>
      <div>
        <h2 className="text-lg font-semibold text-zinc-100">
          {t('dashboard.admin.orders.empty.title')}
        </h2>
        <p className="mt-2 max-w-md text-sm text-zinc-400">
          {t('dashboard.admin.orders.empty.description')}
        </p>
      </div>
    </div>
  );
}

function OrdersError({ error }: { readonly error: ApiError }) {
  return (
    <Alert variant="error">
      <div className="flex flex-col gap-3">
        <div>
          <p className="font-semibold">{t('dashboard.admin.orders.error.title')}</p>
          <p className="mt-1 text-sm opacity-90">{resolveErrorMessage(error)}</p>
        </div>
        <Link
          href={ROUTE_PATH}
          className="inline-flex w-fit items-center gap-2 rounded-xl border border-red-800/50 px-4 py-2 text-sm font-semibold text-red-300 transition-colors hover:bg-red-950"
        >
          {t('dashboard.admin.orders.error.retry')}
        </Link>
      </div>
    </Alert>
  );
}
