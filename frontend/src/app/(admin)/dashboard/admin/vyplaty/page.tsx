import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import {
  ADMIN_OPS_LIST_DEFAULT_PAGE_SIZE,
  type AdminOpsPage,
  type AdminPayoutBatch,
  getPayoutBatches,
  getProcessingPayoutsCount,
} from '@/lib/api-client-helpers/admin-ops-client';
import { t } from '@/lib/i18n';
import { OpsPagination } from '../ops-pagination';
import { PayoutBatchCard } from './payout-batch-card';

/**
 * Admin payout view (T-0118c §3 / T-0127 re-wire, US-admin-0007). Server
 * Component, `force-dynamic`. VIEW + complete-action + operator CSV; NO
 * manual create-batch (A.3 — the T-0104 weekly timer + its HTTP
 * escape-hatch own creation). The CSV is the cross-maker bank file —
 * admin/operator-only, INVERTING the T-0116 maker absence (A.4).
 *
 * T-0127 closed the list gap: the page now renders the browsable paged
 * payout-batch LIST (cross-maker / Unscoped, Processing + Completed,
 * `CreatedAt DESC`) with URL-state pagination (T-0087a). The operator
 * browses + completes/downloads CSV per row by VISIBLE id instead of
 * pasting one blind. The processing count tile stays as the at-a-glance KPI.
 */

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.admin.ops.payouts.metadata.title'),
    description: t('dashboard.admin.ops.payouts.metadata.description'),
  };
}

export const dynamic = 'force-dynamic';

const ROUTE_PATH = '/dashboard/admin/vyplaty';

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

export default async function AdminPayoutsPage({ searchParams }: PageProps) {
  const sp = await searchParams;
  const page = parsePositiveInt(readString(sp.page), 1);

  const [countResult, listResult] = await Promise.all([
    getProcessingPayoutsCount(),
    getPayoutBatches(page, ADMIN_OPS_LIST_DEFAULT_PAGE_SIZE),
  ]);

  const countUnauthorized = !countResult.success && countResult.error.type === 'Unauthorized';
  const listUnauthorized = !listResult.success && listResult.error.type === 'Unauthorized';
  if (countUnauthorized || listUnauthorized) {
    redirect(`/admin/login?redirect=${encodeURIComponent(ROUTE_PATH)}`);
  }

  const count = countResult.success ? countResult.value : null;
  const list = listResult.success ? listResult.value : null;

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-4xl px-4 sm:px-6 lg:px-8">
        <header className="mb-8">
          <div className="flex items-center gap-3">
            <span className="icon-tile h-10 w-10 shrink-0" aria-hidden="true">
              <Icon name="wallet" size={18} />
            </span>
            <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
              {t('dashboard.admin.ops.payouts.title')}
            </h1>
          </div>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.admin.ops.payouts.subtitle')}
          </p>
        </header>

        <Card className="mb-6 flex items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <span className="text-zinc-500">
              <Icon name="creditCard" size={20} />
            </span>
            <span className="text-sm font-medium text-zinc-400">
              {t('dashboard.admin.ops.payouts.processingCount.label')}
            </span>
          </div>
          <span className="text-3xl font-bold text-white">{count === null ? '—' : count}</span>
        </Card>

        {count === null ? (
          <Alert variant="warning" className="mb-6">
            <p className="text-sm">{t('dashboard.admin.ops.payouts.processingCount.unavailable')}</p>
          </Alert>
        ) : null}

        <PayoutList list={list} />
      </div>
    </section>
  );
}

function PayoutList({
  list,
}: {
  readonly list: AdminOpsPage<AdminPayoutBatch> | null;
}) {
  if (list === null) {
    return (
      <Alert variant="error">
        <p className="font-semibold">{t('dashboard.admin.ops.payouts.list.error.title')}</p>
        <p className="mt-1 text-sm opacity-90">
          {t('dashboard.admin.ops.payouts.list.error.body')}
        </p>
      </Alert>
    );
  }

  if (list.totalCount === 0) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 rounded-xl border border-dashed border-zinc-800 bg-surface-card px-6 py-16 text-center">
        <span className="icon-tile h-14 w-14" aria-hidden="true">
          <Icon name="creditCard" size={28} />
        </span>
        <div>
          <h2 className="text-lg font-semibold text-zinc-100">
            {t('dashboard.admin.ops.payouts.list.empty.title')}
          </h2>
          <p className="mt-2 max-w-md text-sm text-zinc-400">
            {t('dashboard.admin.ops.payouts.list.empty.body')}
          </p>
        </div>
      </div>
    );
  }

  const totalPages = list.totalPages ?? 1;

  return (
    <>
      <div className="rounded-xl border border-zinc-800 bg-surface-card">
        <div className="flex items-center justify-between gap-3 rounded-t-xl border-b border-zinc-800 bg-surface-secondary/60 px-4 py-3">
          <div className="flex items-center gap-2.5">
            <h2 className="text-sm font-semibold text-zinc-100">
              {t('dashboard.admin.ops.payouts.title')}
            </h2>
            <Badge
              dot={false}
              aria-label={t('dashboard.admin.ops.payouts.list.count', { count: list.totalCount })}
            >
              {list.totalCount}
            </Badge>
          </div>
        </div>
        <ul className="divide-y divide-zinc-800">
          {list.items.map((batch) => (
            <li key={batch.batchId}>
              <PayoutBatchCard batch={batch} />
            </li>
          ))}
        </ul>
      </div>

      <OpsPagination
        page={list.page}
        totalPages={totalPages}
        hasNext={list.hasNextPage ?? false}
        hasPrevious={list.hasPreviousPage ?? false}
        routePath={ROUTE_PATH}
      />
    </>
  );
}
