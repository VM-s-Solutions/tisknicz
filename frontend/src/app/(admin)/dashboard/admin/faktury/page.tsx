import Link from 'next/link';
import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Icon } from '@/components/ui/icon';
import {
  ADMIN_LIST_DEFAULT_PAGE_SIZE,
  ADMIN_LIST_MAX_PAGE_SIZE,
  type AdminInvoicesInput,
  type AdminInvoicesPage,
  getAdminInvoices,
} from '@/lib/api-client-helpers/admin-client';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';
import type { ApiError } from '@/lib/runtime/result';
import { InvoiceFilters } from './invoice-filters';
import { InvoiceRows } from './invoice-row';
import { AdminPagination } from '../_components/admin-pagination';
import { parsePage, parsePageSize, retryHref } from '../_components/list-params';

/**
 * Admin all-invoices list (T-0118a, US-admin-0012 AC-1). Server
 * Component: filters/page live in URL searchParams, the SSR fetch
 * forwards the admin-audience cookie. Sorted `IssueDate DESC`
 * server-side. The per-row "Stáhnout fakturu" download ships disabled
 * (the admin invoice-PDF endpoint is a logged backend follow-up — see
 * invoice-download.tsx). Junk params degrade to defaults.
 */

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.admin.invoices.metadata.title'),
    description: t('dashboard.admin.invoices.metadata.description'),
  };
}

export const dynamic = 'force-dynamic';

const ROUTE_PATH = '/dashboard/admin/faktury';
/** InvoiceType ordinals exposed in the filter: 0 = Customer, 1 = Fee. */
const INVOICE_TYPE_VALUES = new Set<string>(['0', '1']);
const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

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

export default async function AdminInvoicesPage({ searchParams }: PageProps) {
  const sp = await searchParams;

  const page = parsePage(sp.page);
  const pageSize = parsePageSize(sp.pageSize, ADMIN_LIST_DEFAULT_PAGE_SIZE, ADMIN_LIST_MAX_PAGE_SIZE);
  const rawType = readString(sp.type);
  const rawCountry = readString(sp.country).trim().toUpperCase();
  const rawRecipient = readString(sp.recipient).trim();
  const rawDateFrom = readString(sp.dateFrom);
  const rawDateTo = readString(sp.dateTo);

  const type = INVOICE_TYPE_VALUES.has(rawType) ? Number.parseInt(rawType, 10) : undefined;
  const country = rawCountry !== '' ? rawCountry : undefined;
  const recipient = rawRecipient !== '' ? rawRecipient : undefined;
  const dateFrom = ISO_DATE_PATTERN.test(rawDateFrom) ? rawDateFrom : undefined;
  const dateTo = ISO_DATE_PATTERN.test(rawDateTo) ? rawDateTo : undefined;

  const input: AdminInvoicesInput = { page, pageSize, type, country, recipient, dateFrom, dateTo };
  const result = await getAdminInvoices(input);

  if (!result.success && result.error.type === 'Unauthorized') {
    redirect(`/admin/login?redirect=${encodeURIComponent(ROUTE_PATH)}`);
  }

  const paginationParams: Record<string, string> = {};
  if (type !== undefined) paginationParams.type = String(type);
  if (country) paginationParams.country = country;
  if (recipient) paginationParams.recipient = recipient;
  if (dateFrom) paginationParams.dateFrom = dateFrom;
  if (dateTo) paginationParams.dateTo = dateTo;
  if (pageSize !== ADMIN_LIST_DEFAULT_PAGE_SIZE) paginationParams.pageSize = String(pageSize);

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <header className="mb-8">
          <div className="flex items-center gap-3">
            <span className="icon-tile h-10 w-10 shrink-0" aria-hidden="true">
              <Icon name="receipt" size={18} />
            </span>
            <h1 className="text-3xl font-bold tracking-tight text-zinc-50 sm:text-4xl">
              {t('dashboard.admin.invoices.title')}
            </h1>
          </div>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.admin.invoices.subtitle')}
          </p>
        </header>

        <InvoiceFilters
          type={type !== undefined ? String(type) : ''}
          country={country ?? ''}
          recipient={recipient ?? ''}
          dateFrom={dateFrom ?? ''}
          dateTo={dateTo ?? ''}
        />

        <div className="mt-8">
          {result.success ? (
            <InvoicesResults data={result.value} baseParams={paginationParams} />
          ) : (
            <InvoicesError error={result.error} retryHref={retryHref(ROUTE_PATH, paginationParams, page)} />
          )}
        </div>
      </div>
    </section>
  );
}

interface InvoicesResultsProps {
  readonly data: AdminInvoicesPage;
  readonly baseParams: Readonly<Record<string, string>>;
}

function InvoicesResults({ data, baseParams }: InvoicesResultsProps) {
  if (data.totalCount === 0) {
    return <InvoicesEmpty hasFilters={Object.keys(baseParams).length > 0} />;
  }

  const totalPages = data.totalPages ?? 1;
  const hasNext = data.hasNextPage ?? false;
  const hasPrevious = data.hasPreviousPage ?? false;

  return (
    <>
      <InvoiceRows items={data.items} totalCount={data.totalCount} />
      <AdminPagination
        routePath="/dashboard/admin/faktury"
        page={data.page}
        totalPages={totalPages}
        hasNext={hasNext}
        hasPrevious={hasPrevious}
        baseParams={baseParams}
      />
    </>
  );
}

/**
 * T-0175 (audit ADM-L1): a filtered-to-zero list used to claim nothing
 * exists at all and offered no way back — distinguish the two and give
 * the filtered case an in-place reset.
 */
function InvoicesEmpty({ hasFilters }: { readonly hasFilters: boolean }) {
  if (hasFilters) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 rounded-xl border border-dashed border-zinc-800 bg-surface-card px-6 py-20 text-center">
        <span className="icon-tile h-14 w-14" aria-hidden="true">
          <Icon name="search" size={28} />
        </span>
        <div>
          <h2 className="text-lg font-semibold text-zinc-100">
            {t('dashboard.admin.list.filtered_empty.title')}
          </h2>
          <p className="mt-2 max-w-md text-sm text-zinc-400">
            {t('dashboard.admin.list.filtered_empty.description')}
          </p>
        </div>
        <Link
          href="/dashboard/admin/faktury"
          className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-4 py-2 text-sm font-semibold text-zinc-300 transition-colors hover:border-zinc-600 hover:bg-zinc-800"
        >
          {t('dashboard.admin.list.filtered_empty.reset')}
        </Link>
      </div>
    );
  }

  return (
    <div className="flex flex-col items-center justify-center gap-4 rounded-xl border border-dashed border-zinc-800 bg-surface-card px-6 py-20 text-center">
      <span className="icon-tile h-14 w-14" aria-hidden="true">
        <Icon name="file" size={28} />
      </span>
      <div>
        <h2 className="text-lg font-semibold text-zinc-100">
          {t('dashboard.admin.invoices.empty.title')}
        </h2>
        <p className="mt-2 max-w-md text-sm text-zinc-400">
          {t('dashboard.admin.invoices.empty.description')}
        </p>
      </div>
    </div>
  );
}

function InvoicesError({ error, retryHref }: { readonly error: ApiError; readonly retryHref: string }) {
  return (
    <Alert variant="error">
      <div className="flex flex-col gap-3">
        <div>
          <p className="font-semibold">{t('dashboard.admin.invoices.error.title')}</p>
          <p className="mt-1 text-sm opacity-90">{resolveErrorMessage(error)}</p>
        </div>
        <Link
          href={retryHref}
          className="inline-flex w-fit items-center gap-2 rounded-lg border border-error/40 px-4 py-2 text-sm font-semibold text-error transition-colors hover:bg-error/15"
        >
          {t('dashboard.admin.invoices.error.retry')}
        </Link>
      </div>
    </Alert>
  );
}
