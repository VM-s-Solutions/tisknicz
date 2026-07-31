import Link from 'next/link';
import type { Metadata } from 'next';
import { redirect } from 'next/navigation';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { getAdminOrders, OrderState } from '@/lib/api-client-helpers/admin-client';
import {
  getProcessingPayoutsCount,
  getStalledOutboxCount,
} from '@/lib/api-client-helpers/admin-ops-client';
import { t } from '@/lib/i18n';
import type { MessageKey } from '@/lib/i18n';

/**
 * Admin overview (T-0118a, US-admin-0002 + T-0118c re-wire). Server
 * Component: KPI tiles each deep-link into a read list or an action
 * surface.
 *
 * Order-by-state counts come from the EXISTING all-orders read
 * (T-0105) — a thin `pageSize: 1` request per state reads the
 * server-computed `totalCount` for that state filter (NOT an unpaginated
 * over-fetch; A.2 / Option B rejected). The `Processing`-payout and
 * stalled-outbox tiles are now BACKED by the T-0126 count endpoints
 * (re-wired in T-0118c — they were "—" placeholders in T-0118a before the
 * contracts existed) and deep-link to their ops surfaces. Any count whose
 * read fails renders "—" gracefully (AC-4 — never throws).
 */

export function generateMetadata(): Metadata {
  return {
    title: t('dashboard.admin.overview.metadata.title'),
    description: t('dashboard.admin.overview.metadata.description'),
  };
}

export const dynamic = 'force-dynamic';

const ROUTE_PATH = '/dashboard/admin';

/** A `pageSize: 1` count probe off the existing all-orders read — `null` on failure (graceful "—"). */
async function countOrdersInState(state: OrderState): Promise<number | null> {
  const result = await getAdminOrders({ page: 1, pageSize: 1, state });
  if (!result.success) {
    if (result.error.type === 'Unauthorized') {
      redirect(`/admin/login?redirect=${encodeURIComponent(ROUTE_PATH)}`);
    }
    return null;
  }
  return result.value.totalCount;
}

/** T-0126 ops count → `null` on failure (graceful "—"). */
async function readCount(
  fetcher: () => Promise<{ success: true; value: number } | { success: false; error: { type: string } }>,
): Promise<number | null> {
  const result = await fetcher();
  if (!result.success) {
    if (result.error.type === 'Unauthorized') {
      redirect(`/admin/login?redirect=${encodeURIComponent(ROUTE_PATH)}`);
    }
    return null;
  }
  return result.value;
}

export default async function AdminOverviewPage() {
  // Parallel count probes — one round-trip each off the existing reads + the
  // T-0126 ops count endpoints. The probes are independent, so Promise.all
  // collapses the waterfall to ~1 RTT (Gate 8 fold).
  const [paid, accepted, shipped, disputed, processingPayouts, stalledOutbox] = await Promise.all([
    countOrdersInState(OrderState.Paid),
    countOrdersInState(OrderState.Accepted),
    countOrdersInState(OrderState.Shipped),
    countOrdersInState(OrderState.Disputed),
    readCount(getProcessingPayoutsCount),
    readCount(getStalledOutboxCount),
  ]);

  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <header className="mb-8">
          <div className="flex items-center gap-3">
            <span className="icon-tile h-10 w-10 shrink-0" aria-hidden="true">
              <Icon name="barChart" size={18} />
            </span>
            <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
              {t('dashboard.admin.overview.title')}
            </h1>
          </div>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('dashboard.admin.overview.subtitle')}
          </p>
        </header>

        <h2 className="mb-4 text-xs font-semibold uppercase tracking-widest text-zinc-500">
          {t('dashboard.admin.overview.orders.heading')}
        </h2>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <KpiTile
            labelKey="dashboard.admin.overview.tile.paid"
            count={paid}
            href={`/dashboard/admin/orders?state=${OrderState.Paid}`}
            icon="creditCard"
          />
          <KpiTile
            labelKey="dashboard.admin.overview.tile.accepted"
            count={accepted}
            href={`/dashboard/admin/orders?state=${OrderState.Accepted}`}
            icon="checkCircle"
          />
          <KpiTile
            labelKey="dashboard.admin.overview.tile.shipped"
            count={shipped}
            href={`/dashboard/admin/orders?state=${OrderState.Shipped}`}
            icon="truck"
          />
          <KpiTile
            labelKey="dashboard.admin.overview.tile.disputed"
            count={disputed}
            href={`/dashboard/admin/orders?state=${OrderState.Disputed}`}
            icon="alertCircle"
            emphasis={disputed !== null && disputed > 0}
          />
        </div>

        <h2 className="mb-4 mt-10 text-xs font-semibold uppercase tracking-widest text-zinc-500">
          {t('dashboard.admin.overview.ops.heading')}
        </h2>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          {/* T-0126-backed: Processing-payout + stalled-outbox counts now
              deep-link to their ops surfaces (re-wired in T-0118c). */}
          <KpiTile
            labelKey="dashboard.admin.overview.tile.payouts"
            count={processingPayouts}
            href="/dashboard/admin/vyplaty"
            icon="creditCard"
          />
          <KpiTile
            labelKey="dashboard.admin.overview.tile.outbox"
            count={stalledOutbox}
            href="/dashboard/admin/outbox"
            icon="clock"
            emphasis={stalledOutbox !== null && stalledOutbox > 0}
          />
        </div>
      </div>
    </section>
  );
}

interface KpiTileProps {
  readonly labelKey: MessageKey;
  /** `null` → unavailable count, rendered as "—" (AC-4 graceful placeholder). */
  readonly count: number | null;
  readonly href: string;
  readonly icon: 'creditCard' | 'checkCircle' | 'truck' | 'alertCircle' | 'clock';
  readonly emphasis?: boolean;
}

function KpiTile({ labelKey, count, href, icon, emphasis = false }: KpiTileProps) {
  const unavailable = count === null;
  return (
    <Link href={href} className="block">
      <Card
        hover
        className={`flex h-full flex-col gap-4 ${emphasis ? 'border-red-900/50' : ''}`}
      >
        <div className="flex items-center justify-between">
          <span className="text-sm font-medium text-zinc-400">{t(labelKey)}</span>
          <span className={emphasis ? 'text-red-400' : 'text-zinc-500'}>
            <Icon name={icon} size={20} />
          </span>
        </div>
        <span
          className={`text-3xl font-bold ${emphasis ? 'text-red-400' : 'text-white'}`}
          aria-label={unavailable ? t('dashboard.admin.overview.tile.unavailableAria') : undefined}
        >
          {unavailable ? '—' : count}
        </span>
        <span className="text-sm text-brand-400">
          {t('dashboard.admin.overview.tile.viewList')}
        </span>
      </Card>
    </Link>
  );
}
