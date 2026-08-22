import Link from 'next/link';
import { Card } from '@/components/ui/card';
import { Icon, type IconName } from '@/components/ui/icon';
import { type PlatformRevenue, RevenueWindow } from '@/lib/api-client-helpers/admin-ops-client';
import { t } from '@/lib/i18n';
import type { MessageKey } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { readParam } from './_components/list-params';

/**
 * Admin overview earnings panel (T-0186): what the platform made on sales
 * over a rolling window, with a day / week / month switch.
 *
 * Server Component. The switch is three `<Link>`s over `?earnings=`, not a
 * client island — the URL is the state container for admin filters
 * (CLAUDE.md §4), so a chosen window survives a refresh and is shareable
 * with whoever asked for the number.
 *
 * Every amount arrives from the backend in minor units and is only
 * formatted here; the panel does no money arithmetic — not even the
 * refund subtraction, which the backend deliberately leaves un-netted
 * (a refund is a gross amount and does not decompose into a platform
 * share and a maker share).
 */

/** URL param carrying the chosen window. */
export const EARNINGS_PARAM = 'earnings';

const WINDOW_LABELS: Readonly<Record<RevenueWindow, MessageKey>> = {
  [RevenueWindow.Day]: 'dashboard.admin.overview.earnings.window.day',
  [RevenueWindow.Week]: 'dashboard.admin.overview.earnings.window.week',
  [RevenueWindow.Month]: 'dashboard.admin.overview.earnings.window.month',
};

const WINDOW_ORDER: readonly RevenueWindow[] = [
  RevenueWindow.Day,
  RevenueWindow.Week,
  RevenueWindow.Month,
];

/**
 * Reads `?earnings=` into a window. Anything unrecognised falls back to
 * Day rather than reaching the API — the backend Validator would reject it
 * anyway, and a hand-typed param must not blank the panel.
 */
export function parseRevenueWindow(raw: string | string[] | undefined): RevenueWindow {
  const value = readParam(raw);
  return WINDOW_ORDER.find((w) => w === value) ?? RevenueWindow.Day;
}

interface EarningsPanelProps {
  readonly window: RevenueWindow;
  /** `null` when the read failed — the panel says so instead of showing zeros as if they were real. */
  readonly revenue: PlatformRevenue | null;
}

export function EarningsPanel({ window, revenue }: EarningsPanelProps) {
  return (
    <section aria-labelledby="admin-earnings-heading">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2
          id="admin-earnings-heading"
          className="text-xs font-semibold uppercase tracking-widest text-zinc-500"
        >
          {t('dashboard.admin.overview.earnings.heading')}
        </h2>
        <nav
          className="flex flex-wrap items-center gap-1"
          aria-label={t('dashboard.admin.overview.earnings.windowAria')}
        >
          {WINDOW_ORDER.map((option) => {
            const active = option === window;
            return (
              <Link
                key={option}
                href={`?${EARNINGS_PARAM}=${option}`}
                scroll={false}
                aria-current={active ? 'true' : undefined}
                className={`rounded-lg border px-3 py-1.5 text-sm font-medium whitespace-nowrap transition-colors ${
                  active
                    ? 'border-brand-500/40 bg-brand-400/10 text-brand-200'
                    : 'border-transparent text-zinc-400 hover:border-zinc-700 hover:bg-zinc-800/50 hover:text-zinc-100'
                }`}
              >
                {t(WINDOW_LABELS[option])}
              </Link>
            );
          })}
        </nav>
      </div>

      {revenue === null ? (
        <Card>
          <p className="text-sm text-zinc-400">
            {t('dashboard.admin.overview.earnings.unavailable')}
          </p>
        </Card>
      ) : (
        <>
          {/* The commission is the question being asked, so it gets the
              whole first row and the largest type; the rest is context. */}
          <Card className="flex flex-col gap-2">
            <div className="flex items-center justify-between gap-3">
              <span className="text-sm font-medium text-zinc-400">
                {t('dashboard.admin.overview.earnings.fee')}
              </span>
              <span className="text-zinc-500">
                <Icon name="wallet" size={20} />
              </span>
            </div>
            <span className="text-4xl font-bold tracking-tight text-white">
              {formatCzk(revenue.platformFeeMinor, revenue.currency)}
            </span>
            <span className="text-sm text-zinc-500">
              {t(WINDOW_LABELS[window])} · {t('dashboard.admin.overview.earnings.feeNote')}
            </span>
          </Card>

          <div className="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <AmountTile
              labelKey="dashboard.admin.overview.earnings.gross"
              noteKey="dashboard.admin.overview.earnings.grossNote"
              value={formatCzk(revenue.grossVolumeMinor, revenue.currency)}
              icon="creditCard"
            />
            <AmountTile
              labelKey="dashboard.admin.overview.earnings.payout"
              noteKey="dashboard.admin.overview.earnings.payoutNote"
              value={formatCzk(revenue.makerPayoutMinor, revenue.currency)}
              icon="users"
            />
            <AmountTile
              labelKey="dashboard.admin.overview.earnings.orders"
              noteKey="dashboard.admin.overview.earnings.ordersNote"
              value={String(revenue.paidOrderCount)}
              icon="package"
            />
            <AmountTile
              labelKey="dashboard.admin.overview.earnings.refunded"
              noteKey="dashboard.admin.overview.earnings.refundedNote"
              value={formatCzk(revenue.refundedMinor, revenue.currency)}
              icon="refresh"
            />
          </div>

          <p className="mt-3 text-xs text-zinc-500">
            {t('dashboard.admin.overview.earnings.basis')}
          </p>
        </>
      )}
    </section>
  );
}

interface AmountTileProps {
  readonly labelKey: MessageKey;
  readonly noteKey: MessageKey;
  readonly value: string;
  readonly icon: IconName;
}

function AmountTile({ labelKey, noteKey, value, icon }: AmountTileProps) {
  return (
    <Card className="flex h-full flex-col gap-3">
      <div className="flex items-center justify-between gap-3">
        <span className="text-sm font-medium text-zinc-400">{t(labelKey)}</span>
        <span className="text-zinc-500">
          <Icon name={icon} size={18} />
        </span>
      </div>
      <span className="text-2xl font-bold text-white">{value}</span>
      <span className="text-xs text-zinc-500">{t(noteKey)}</span>
    </Card>
  );
}
