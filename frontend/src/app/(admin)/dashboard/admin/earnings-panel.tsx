import Link from 'next/link';
import { Card } from '@/components/ui/card';
import { Icon, type IconName } from '@/components/ui/icon';
import type { PlatformRevenue } from '@/lib/api-client-helpers/admin-ops-client';
import {
  formatReportingMonth,
  shiftMonth,
  toMonthParam,
} from '@/lib/format/reporting-period';
import { t } from '@/lib/i18n';
import type { MessageKey } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';

/**
 * Admin overview earnings panel: what the platform made on sales in ONE
 * CALENDAR MONTH, with a previous/next month navigator.
 *
 * T-0192 replaced T-0186's rolling day/week/month windows. "The last 30
 * days" is a number nobody can reconcile — it matches no invoice run and no
 * VAT period, and it changes every time the page is refreshed. A month is
 * the unit the business already accounts in, so the panel answers for a
 * month and the operator pages between them. Trend lives in the chart below,
 * which is where a rolling view actually belongs.
 *
 * Server Component. The navigator is two `<Link>`s over `?month=YYYY-MM`,
 * not a client island — the URL is the state container for admin filters
 * (CLAUDE.md §4), so a chosen month survives a refresh and is shareable with
 * whoever asked for the number.
 *
 * Every amount arrives from the backend in minor units and is only formatted
 * here; the panel does no money arithmetic — not even the refund
 * subtraction, which the backend deliberately leaves un-netted (a refund is
 * a gross amount and does not decompose into a platform share and a maker
 * share).
 */

/** URL param carrying the chosen month, `YYYY-MM`. */
export const MONTH_PARAM = 'month';

interface EarningsPanelProps {
  /** `null` when the read failed — the panel says so instead of showing zeros as if they were real. */
  readonly revenue: PlatformRevenue | null;
  /** Preserved across month links so switching months never resets the chart. */
  readonly extraParams?: Readonly<Record<string, string>>;
}

function monthHref(
  year: number,
  month: number,
  extraParams: Readonly<Record<string, string>>,
): string {
  const params = new URLSearchParams(extraParams);
  params.set(MONTH_PARAM, toMonthParam(year, month));
  return `?${params.toString()}`;
}

export function EarningsPanel({ revenue, extraParams = {} }: EarningsPanelProps) {
  return (
    <section aria-labelledby="admin-earnings-heading">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2
          id="admin-earnings-heading"
          className="text-xs font-semibold uppercase tracking-widest text-zinc-500"
        >
          {t('dashboard.admin.overview.earnings.heading')}
        </h2>
        {revenue !== null && (
          <MonthNavigator
            year={revenue.year}
            month={revenue.month}
            isCurrentMonth={revenue.isCurrentMonth}
            extraParams={extraParams}
          />
        )}
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
            <span className="text-4xl font-bold tracking-tight text-zinc-50">
              {formatCzk(revenue.platformFeeMinor, revenue.currency)}
            </span>
            <span className="text-sm text-zinc-500">
              {formatReportingMonth(revenue.year, revenue.month)}
              {revenue.isCurrentMonth
                ? ` · ${t('dashboard.admin.overview.earnings.monthInProgress')}`
                : ''}{' '}
              · {t('dashboard.admin.overview.earnings.feeNote')}
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

interface MonthNavigatorProps {
  readonly year: number;
  readonly month: number;
  /** Disables "next" — there is nothing to report from a month that has not started. */
  readonly isCurrentMonth: boolean;
  readonly extraParams: Readonly<Record<string, string>>;
}

function MonthNavigator({ year, month, isCurrentMonth, extraParams }: MonthNavigatorProps) {
  const previous = shiftMonth(year, month, -1);
  const next = shiftMonth(year, month, 1);
  const arrow =
    'flex h-8 w-8 items-center justify-center rounded-lg border border-zinc-800 text-zinc-400 transition-colors hover:bg-zinc-800/50 hover:text-zinc-100';

  return (
    <nav
      className="flex items-center gap-2"
      aria-label={t('dashboard.admin.overview.earnings.monthAria')}
    >
      <Link
        href={monthHref(previous.year, previous.month, extraParams)}
        scroll={false}
        className={arrow}
        aria-label={t('dashboard.admin.overview.earnings.previousMonth')}
      >
        <Icon name="chevronLeft" size={16} />
      </Link>
      <span
        className="min-w-36 text-center text-sm font-medium text-zinc-100"
        aria-live="polite"
      >
        {formatReportingMonth(year, month)}
      </span>
      {isCurrentMonth ? (
        // A month that has not started has nothing to report, so the control
        // is absent rather than a link that lands on a page of zeros.
        <span
          className={`${arrow} cursor-not-allowed opacity-40`}
          aria-disabled="true"
          aria-label={t('dashboard.admin.overview.earnings.nextMonthUnavailable')}
        >
          <Icon name="chevronRight" size={16} />
        </span>
      ) : (
        <Link
          href={monthHref(next.year, next.month, extraParams)}
          scroll={false}
          className={arrow}
          aria-label={t('dashboard.admin.overview.earnings.nextMonth')}
        >
          <Icon name="chevronRight" size={16} />
        </Link>
      )}
    </nav>
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
      <span className="text-2xl font-bold text-zinc-50">{value}</span>
      <span className="text-xs text-zinc-500">{t(noteKey)}</span>
    </Card>
  );
}
