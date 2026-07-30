import Link from 'next/link';
import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Icon } from '@/components/ui/icon';
import {
  ADMIN_LIST_DEFAULT_PAGE_SIZE,
  ADMIN_LIST_MAX_PAGE_SIZE,
  type AdminAuditInput,
  type AdminAuditPage,
  getAdminAuditLog,
} from '@/lib/api-client-helpers/admin-client';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';
import type { ApiError } from '@/lib/runtime/result';
import { AuditFilters } from './audit-filters';
import { AuditRows } from './audit-row';
import { Pagination } from './pagination';

/**
 * Admin audit-log list (T-0118a, US-admin-0015 AC-1). Server Component:
 * filters/page live in URL searchParams, the SSR fetch forwards the
 * admin-audience cookie. Sorted `created_at DESC` server-side. Each row
 * links forward to the slice-c diff detail. Junk params degrade to
 * defaults.
 */

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.admin.audit.metadata.title'),
    description: t('dashboard.admin.audit.metadata.description'),
  };
}

export const dynamic = 'force-dynamic';

const ROUTE_PATH = '/dashboard/admin/audit';
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

export default async function AdminAuditPage({ searchParams }: PageProps) {
  const sp = await searchParams;

  const page = parsePositiveInt(readString(sp.page), 1, Number.MAX_SAFE_INTEGER);
  const pageSize = parsePositiveInt(
    readString(sp.pageSize),
    ADMIN_LIST_DEFAULT_PAGE_SIZE,
    ADMIN_LIST_MAX_PAGE_SIZE,
  );
  const rawAdminUserId = readString(sp.adminUserId).trim();
  const rawActionCode = readString(sp.actionCode).trim();
  const rawTargetEntity = readString(sp.targetEntity).trim();
  const rawDateFrom = readString(sp.dateFrom);
  const rawDateTo = readString(sp.dateTo);

  const adminUserId = rawAdminUserId !== '' ? rawAdminUserId : undefined;
  const actionCode = rawActionCode !== '' ? rawActionCode : undefined;
  const targetEntity = rawTargetEntity !== '' ? rawTargetEntity : undefined;
  const dateFrom = ISO_DATE_PATTERN.test(rawDateFrom) ? rawDateFrom : undefined;
  const dateTo = ISO_DATE_PATTERN.test(rawDateTo) ? rawDateTo : undefined;

  const input: AdminAuditInput = { page, pageSize, adminUserId, actionCode, targetEntity, dateFrom, dateTo };
  const result = await getAdminAuditLog(input);

  if (!result.success && result.error.type === 'Unauthorized') {
    redirect(`/admin/login?redirect=${encodeURIComponent(ROUTE_PATH)}`);
  }

  const paginationParams: Record<string, string> = {};
  if (adminUserId) paginationParams.adminUserId = adminUserId;
  if (actionCode) paginationParams.actionCode = actionCode;
  if (targetEntity) paginationParams.targetEntity = targetEntity;
  if (dateFrom) paginationParams.dateFrom = dateFrom;
  if (dateTo) paginationParams.dateTo = dateTo;
  if (pageSize !== ADMIN_LIST_DEFAULT_PAGE_SIZE) paginationParams.pageSize = String(pageSize);

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <header className="mb-8">
          <div className="flex items-center gap-3">
            <span className="icon-tile h-10 w-10 shrink-0" aria-hidden="true">
              <Icon name="shield" size={18} />
            </span>
            <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
              {t('dashboard.admin.audit.title')}
            </h1>
          </div>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.admin.audit.subtitle')}
          </p>
        </header>

        <AuditFilters
          adminUserId={adminUserId ?? ''}
          actionCode={actionCode ?? ''}
          targetEntity={targetEntity ?? ''}
          dateFrom={dateFrom ?? ''}
          dateTo={dateTo ?? ''}
        />

        <div className="mt-8">
          {result.success ? (
            <AuditResults data={result.value} baseParams={paginationParams} />
          ) : (
            <AuditError error={result.error} />
          )}
        </div>
      </div>
    </section>
  );
}

interface AuditResultsProps {
  readonly data: AdminAuditPage;
  readonly baseParams: Readonly<Record<string, string>>;
}

function AuditResults({ data, baseParams }: AuditResultsProps) {
  if (data.totalCount === 0) {
    return <AuditEmpty />;
  }

  const totalPages = data.totalPages ?? 1;
  const hasNext = data.hasNextPage ?? false;
  const hasPrevious = data.hasPreviousPage ?? false;

  return (
    <>
      <p className="mb-6 text-sm text-zinc-500">
        {t('dashboard.admin.audit.count', { count: data.totalCount })}
      </p>
      <AuditRows items={data.items} />
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

function AuditEmpty() {
  return (
    <div className="flex flex-col items-center justify-center gap-4 rounded-2xl border border-dashed border-zinc-800 bg-surface-card px-6 py-20 text-center">
      <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-zinc-800 text-zinc-500">
        <Icon name="file" size={28} />
      </div>
      <div>
        <h2 className="text-lg font-semibold text-zinc-100">
          {t('dashboard.admin.audit.empty.title')}
        </h2>
        <p className="mt-2 max-w-md text-sm text-zinc-400">
          {t('dashboard.admin.audit.empty.description')}
        </p>
      </div>
    </div>
  );
}

function AuditError({ error }: { readonly error: ApiError }) {
  return (
    <Alert variant="error">
      <div className="flex flex-col gap-3">
        <div>
          <p className="font-semibold">{t('dashboard.admin.audit.error.title')}</p>
          <p className="mt-1 text-sm opacity-90">{resolveErrorMessage(error)}</p>
        </div>
        <Link
          href={ROUTE_PATH}
          className="inline-flex w-fit items-center gap-2 rounded-xl border border-red-800/50 px-4 py-2 text-sm font-semibold text-red-300 transition-colors hover:bg-red-950"
        >
          {t('dashboard.admin.audit.error.retry')}
        </Link>
      </div>
    </Alert>
  );
}
