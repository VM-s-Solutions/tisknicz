import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { getStalledOutboxCount } from '@/lib/api-client-helpers/admin-ops-client';
import { t } from '@/lib/i18n';
import { OutboxActions } from './outbox-actions';

/**
 * Admin outbox triage (T-0118c §1, US-admin-0014). Server Component,
 * `force-dynamic`: the SSR fetch forwards the admin-audience cookie
 * (patterns.md B.14 / ADR 0024). A customer/maker JWT replayed against the
 * admin host 401s at the backend (ADR 0013) and bounces to /admin/login.
 *
 * CONTRACT GAP (flagged): the admin contract exposes ONLY the stalled
 * COUNT (T-0126) — there is no per-event LIST read. The page therefore
 * renders the count + a by-id action surface (retry / acknowledge are the
 * T-0109 endpoints this slice owns). A thin list endpoint is the clean fix
 * and is logged as a backend follow-up (no backend is added in this
 * frontend bundle, mirroring the T-0118a KPI count follow-ups).
 */

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.admin.ops.outbox.metadata.title'),
    description: t('dashboard.admin.ops.outbox.metadata.description'),
  };
}

export const dynamic = 'force-dynamic';

const ROUTE_PATH = '/dashboard/admin/outbox';

export default async function AdminOutboxPage() {
  const result = await getStalledOutboxCount();

  if (!result.success && result.error.type === 'Unauthorized') {
    redirect(`/admin/login?redirect=${encodeURIComponent(ROUTE_PATH)}`);
  }

  const count = result.success ? result.value : null;

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
        ) : count === 0 ? (
          <Alert variant="success" className="mb-6">
            <p className="text-sm">{t('dashboard.admin.ops.outbox.stalledCount.none')}</p>
          </Alert>
        ) : null}

        <Alert variant="info" className="mb-6">
          <div>
            <p className="font-semibold">{t('dashboard.admin.ops.outbox.listGap.title')}</p>
            <p className="mt-1 text-sm opacity-90">
              {t('dashboard.admin.ops.outbox.listGap.body')}
            </p>
          </div>
        </Alert>

        <OutboxActions />
      </div>
    </section>
  );
}
