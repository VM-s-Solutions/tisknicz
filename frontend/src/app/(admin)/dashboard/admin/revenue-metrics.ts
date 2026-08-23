import {
  type PlatformRevenuePoint,
  RevenueRange,
} from '@/lib/api-client-helpers/admin-ops-client';
import type { MessageKey } from '@/lib/i18n';

/**
 * The measures the revenue chart can plot, and the ranges it can plot them
 * over (T-0192). Shared by the Server Component that reads the URL and the
 * client canvas that draws — kept in its own module so neither imports the
 * other.
 */

/** URL param carrying the chart's span. */
export const RANGE_PARAM = 'range';

/** URL param carrying which measure is plotted. */
export const METRIC_PARAM = 'metric';

/** Whether a measure is money (formatted in CZK) or a plain count. */
export type RevenueValueKind = 'money' | 'count';

export type RevenueMetricKey = 'fee' | 'gross' | 'payout' | 'refunded' | 'orders';

export interface RevenueMetric {
  readonly key: RevenueMetricKey;
  readonly labelKey: MessageKey;
  readonly captionKey: MessageKey;
  readonly valueKind: RevenueValueKind;
  /**
   * Picks this measure off a point. Runs on the SERVER — a function cannot
   * cross the Server/Client boundary, so the client receives plain numbers.
   */
  readonly select: (point: PlatformRevenuePoint) => number;
}

/**
 * Ordered as the operator reads them: what we earned, then what moved
 * through the platform to produce it, then what went back out.
 *
 * <para>
 * Commission leads because it is the question the panel above is already
 * answering — the chart's job is to show how that number got there, so it
 * opens on the same measure rather than making the operator re-find it.
 * </para>
 */
export const REVENUE_METRICS: readonly RevenueMetric[] = [
  {
    key: 'fee',
    labelKey: 'dashboard.admin.overview.chart.metric.fee',
    captionKey: 'dashboard.admin.overview.chart.caption.fee',
    valueKind: 'money',
    select: (p) => p.platformFeeMinor,
  },
  {
    key: 'gross',
    labelKey: 'dashboard.admin.overview.chart.metric.gross',
    captionKey: 'dashboard.admin.overview.chart.caption.gross',
    valueKind: 'money',
    select: (p) => p.grossVolumeMinor,
  },
  {
    key: 'payout',
    labelKey: 'dashboard.admin.overview.chart.metric.payout',
    captionKey: 'dashboard.admin.overview.chart.caption.payout',
    valueKind: 'money',
    select: (p) => p.makerPayoutMinor,
  },
  {
    key: 'refunded',
    labelKey: 'dashboard.admin.overview.chart.metric.refunded',
    captionKey: 'dashboard.admin.overview.chart.caption.refunded',
    valueKind: 'money',
    select: (p) => p.refundedMinor,
  },
  {
    key: 'orders',
    labelKey: 'dashboard.admin.overview.chart.metric.orders',
    captionKey: 'dashboard.admin.overview.chart.caption.orders',
    valueKind: 'count',
    select: (p) => p.paidOrderCount,
  },
];

/**
 * Reads `?metric=`. Anything unrecognised falls back to commission rather
 * than blanking the chart — a hand-typed param is a typo, not a request for
 * an empty panel.
 */
export function parseRevenueMetric(raw: string): RevenueMetric {
  return REVENUE_METRICS.find((m) => m.key === raw) ?? REVENUE_METRICS[0];
}

/**
 * The spans on offer, shortest first — a day through a full year. The
 * backend Validator is authoritative; this list only decides what the UI
 * offers and in what order.
 */
export const REVENUE_RANGES: readonly { range: RevenueRange; labelKey: MessageKey }[] = [
  { range: RevenueRange.Day, labelKey: 'dashboard.admin.overview.chart.range.day' },
  { range: RevenueRange.Week, labelKey: 'dashboard.admin.overview.chart.range.week' },
  { range: RevenueRange.Month, labelKey: 'dashboard.admin.overview.chart.range.month' },
  { range: RevenueRange.Quarter, labelKey: 'dashboard.admin.overview.chart.range.quarter' },
  { range: RevenueRange.HalfYear, labelKey: 'dashboard.admin.overview.chart.range.halfYear' },
  { range: RevenueRange.Year, labelKey: 'dashboard.admin.overview.chart.range.year' },
];

/**
 * Reads `?range=`. Defaults to the last 30 days — long enough to show a
 * trend, short enough that a quiet week is still visible in it. Anything
 * unrecognised falls back rather than reaching the API, which would 400.
 */
export function parseRevenueRange(raw: string): RevenueRange {
  return REVENUE_RANGES.find((r) => r.range === raw)?.range ?? RevenueRange.Month;
}
