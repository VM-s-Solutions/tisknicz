import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import {
  ADMIN_OPS_LIST_DEFAULT_PAGE_SIZE,
  type AdminOpsPage,
  getStalledOutboxCount,
  getStalledOutboxEvents,
  type StalledOutboxEvent,
} from '@/lib/api-client-helpers/admin-ops-client';
import { t } from '@/lib/i18n';
import { formatDateTime } from '@/lib/utils/dates';
import { OpsPagination } from '../ops-pagination';
import { OutboxRowActions } from './outbox-row-actions';

/**
 * Admin outbox triage (T-0118c §1 / T-0127 re-wire, US-admin-0014). Server
 * Component, `force-dynamic`: the SSR fetch forwards the admin-audience
 * cookie (patterns.md B.14 / ADR 0024). A customer/maker JWT replayed
 * against the admin host 401s at the backend (ADR 0013) and bounces to
 * /admin/login.
 *
 * T-0127 closed the list gap: the page now renders the browsable paged
 * stalled-event LIST (the SAME stalled set the count tile reports —
 * `ProcessedAt == null && NextRetryAt == null && LastErrorKind != None`,
 * `CreatedAt DESC`) with URL-state pagination (T-0087a). The operator
 * browses + retries/acknowledges by VISIBLE id (per row) instead of pasting
 * one blind. The count tile stays as the at-a-glance KPI.
 */

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.admin.ops.outbox.metadata.title'),
    description: t('dashboard.admin.ops.outbox.metadata.description'),
  };
}

export const dynamic = 'force-dynamic';

const ROUTE_PATH = '/dashboard/admin/outbox';

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

export default async function AdminOutboxPage({ searchParams }: PageProps) {
  const sp = await searchParams;
  const page = parsePositiveInt(readString(sp.page), 1);

  const [countResult, listResult] = await Promise.all([
    getStalledOutboxCount(),
    getStalledOutboxEvents(page, ADMIN_OPS_LIST_DEFAULT_PAGE_SIZE),
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
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t('dashboard.admin.ops.outbox.title')}
          </h1>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.admin.ops.outbox.subtitle')}
          </p>
        </header>

        <Card className="mb-6 flex items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <span className="text-zinc-500">
              <Icon name="clock" size={20} />
            </span>
            <span className="text-sm font-medium text-zinc-400">
              {t('dashboard.admin.ops.outbox.stalledCount.label')}
            </span>
          </div>
          <span className="text-3xl font-bold text-white">{count === null ? '—' : count}</span>
        </Card>

        {count === null ? (
          <Alert variant="warning" className="mb-6">
            <p className="text-sm">{t('dashboard.admin.ops.outbox.stalledCount.unavailable')}</p>
          </Alert>
        ) : null}

        <OutboxList list={list} />
      </div>
    </section>
  );
}

function OutboxList({
  list,
}: {
  readonly list: AdminOpsPage<StalledOutboxEvent> | null;
}) {
  if (list === null) {
    return (
      <Alert variant="error">
        <p className="font-semibold">{t('dashboard.admin.ops.outbox.list.error.title')}</p>
        <p className="mt-1 text-sm opacity-90">{t('dashboard.admin.ops.outbox.list.error.body')}</p>
      </Alert>
    );
  }

  if (list.totalCount === 0) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 rounded-2xl border border-dashed border-zinc-800 bg-surface-card px-6 py-16 text-center">
        <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-emerald-950/50 text-emerald-400">
          <Icon name="checkCircle" size={28} />
        </div>
        <div>
          <h2 className="text-lg font-semibold text-zinc-100">
            {t('dashboard.admin.ops.outbox.list.empty.title')}
          </h2>
          <p className="mt-2 max-w-md text-sm text-zinc-400">
            {t('dashboard.admin.ops.outbox.list.empty.body')}
          </p>
        </div>
      </div>
    );
  }

  const totalPages = list.totalPages ?? 1;

  return (
    <>
      <p className="mb-4 text-sm text-zinc-500">
        {t('dashboard.admin.ops.outbox.list.count', { count: list.totalCount })}
      </p>
      <ul className="flex flex-col gap-4">
        {list.items.map((event) => (
          <li key={event.id}>
            <Card className="flex flex-col gap-4">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="flex flex-col gap-1">
                  <span className="text-sm font-semibold text-zinc-100">{event.eventType}</span>
                  <span className="font-mono text-xs text-zinc-500">{event.id}</span>
                </div>
                <Badge variant="warning">
                  {t('dashboard.admin.ops.outbox.list.retryCount', { count: event.retryCount })}
                </Badge>
              </div>

              <dl className="grid grid-cols-1 gap-x-8 gap-y-3 sm:grid-cols-2">
                <div className="flex flex-col gap-1">
                  <dt className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
                    {t('dashboard.admin.ops.outbox.list.aggregateId')}
                  </dt>
                  <dd className="break-all font-mono text-sm text-zinc-200">{event.aggregateId}</dd>
                </div>
                <div className="flex flex-col gap-1">
                  <dt className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
                    {t('dashboard.admin.ops.outbox.list.createdAt')}
                  </dt>
                  <dd className="text-sm text-zinc-200">{formatDateTime(event.createdAt)}</dd>
                </div>
                {event.lastErrorCode && event.lastErrorCode.trim() !== '' ? (
                  <div className="flex flex-col gap-1 sm:col-span-2">
                    <dt className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
                      {t('dashboard.admin.ops.outbox.list.lastErrorCode')}
                    </dt>
                    <dd className="break-all font-mono text-sm text-amber-300">
                      {event.lastErrorCode}
                    </dd>
                  </div>
                ) : null}
              </dl>

              <div className="border-t border-zinc-800 pt-4">
                <OutboxRowActions eventId={event.id} />
              </div>
            </Card>
          </li>
        ))}
      </ul>

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
