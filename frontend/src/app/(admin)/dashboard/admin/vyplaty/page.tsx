import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { getProcessingPayoutsCount } from '@/lib/api-client-helpers/admin-ops-client';
import { t } from '@/lib/i18n';
import { PayoutActions } from './payout-actions';

/**
 * Admin payout view (T-0118c §3, US-admin-0007). Server Component,
 * `force-dynamic`. VIEW + complete-action + operator CSV; NO manual
 * create-batch (A.3 — the T-0104 weekly timer + its HTTP escape-hatch own
 * creation). The CSV is the cross-maker bank file — admin/operator-only,
 * INVERTING the T-0116 maker absence (A.4).
 *
 * CONTRACT GAP (flagged): the admin contract exposes the processing COUNT
 * (T-0126) but NO payout-batch LIST read (the generated `payoutBatches()`
 * method is the CREATE POST — A.3 forbids it here). The page therefore
 * renders the processing count + a by-id complete/CSV surface; a list
 * endpoint is the clean fix and is logged as a backend follow-up. With no
 * list, there is no list to paginate, so `?page=` URL-state pagination is
 * deferred to when the list endpoint lands (gap flagged).
 */

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.admin.ops.payouts.metadata.title'),
    description: t('dashboard.admin.ops.payouts.metadata.description'),
  };
}

export const dynamic = 'force-dynamic';

const ROUTE_PATH = '/dashboard/admin/vyplaty';

export default async function AdminPayoutsPage() {
  const result = await getProcessingPayoutsCount();

  if (!result.success && result.error.type === 'Unauthorized') {
    redirect(`/admin/login?redirect=${encodeURIComponent(ROUTE_PATH)}`);
  }

  const count = result.success ? result.value : null;

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-4xl px-4 sm:px-6 lg:px-8">
        <header className="mb-8">
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t('dashboard.admin.ops.payouts.title')}
          </h1>
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
        ) : count === 0 ? (
          <Alert variant="info" className="mb-6">
            <p className="text-sm">{t('dashboard.admin.ops.payouts.processingCount.none')}</p>
          </Alert>
        ) : null}

        <Alert variant="info" className="mb-6">
          <div>
            <p className="font-semibold">{t('dashboard.admin.ops.payouts.listGap.title')}</p>
            <p className="mt-1 text-sm opacity-90">
              {t('dashboard.admin.ops.payouts.listGap.body')}
            </p>
          </div>
        </Alert>

        <PayoutActions />
      </div>
    </section>
  );
}
