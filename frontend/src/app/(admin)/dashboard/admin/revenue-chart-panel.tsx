import Link from 'next/link';
import { Card } from '@/components/ui/card';
import type { PlatformRevenueSeries, RevenueRange } from '@/lib/api-client-helpers/admin-ops-client';
import { formatBucketPeriod } from '@/lib/format/reporting-period';
import { t } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { RevenueChart } from './revenue-chart';
import {
  METRIC_PARAM,
  RANGE_PARAM,
  REVENUE_METRICS,
  REVENUE_RANGES,
  type RevenueMetric,
} from './revenue-metrics';

/**
 * The revenue chart panel (T-0192) — how sales are moving, from the last 24
 * hours to the last 12 months, the way a price chart is read.
 *
 * <para>
 * Server Component. Both controls are `<Link>`s over `?range=` and
 * `?metric=`, so the chart's state lives in the URL (CLAUDE.md §4) and a
 * particular view is shareable with whoever asked about it. The client
 * boundary is only the canvas.
 * </para>
 *
 * <para>
 * The controls sit ABOVE the card rather than inside it, because they scope
 * the whole panel; the month navigator above scopes the tiles, and the two
 * are deliberately separate — a trend line that reset to one point on the
 * first of each month would be useless.
 * </para>
 *
 * <para>
 * A canvas is opaque to a screen reader, so the same numbers ship as a real
 * table underneath it. It is visually hidden but focusable content — not
 * `aria-hidden` decoration — so the chart never gates a value behind a
 * hover.
 * </para>
 */

interface RevenueChartPanelProps {
  /** `null` when the read failed — the panel says so instead of drawing a flat line at zero. */
  readonly series: PlatformRevenueSeries | null;
  readonly range: RevenueRange;
  readonly metric: RevenueMetric;
  /** Preserved across control links so the chosen month survives a range change. */
  readonly extraParams?: Readonly<Record<string, string>>;
}

function hrefWith(
  extraParams: Readonly<Record<string, string>>,
  key: string,
  value: string,
): string {
  const params = new URLSearchParams(extraParams);
  params.set(key, value);
  return `?${params.toString()}`;
}

export function RevenueChartPanel({
  series,
  range,
  metric,
  extraParams = {},
}: RevenueChartPanelProps) {
  const rangeParams = { ...extraParams, [METRIC_PARAM]: metric.key };
  const metricParams = { ...extraParams, [RANGE_PARAM]: range };

  return (
    <section aria-labelledby="admin-revenue-chart-heading">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2
          id="admin-revenue-chart-heading"
          className="text-xs font-semibold uppercase tracking-widest text-zinc-500"
        >
          {t('dashboard.admin.overview.chart.heading')}
        </h2>
        <nav
          className="flex flex-wrap items-center gap-1"
          aria-label={t('dashboard.admin.overview.chart.rangeAria')}
        >
          {REVENUE_RANGES.map((option) => (
            <Pill
              key={option.range}
              href={hrefWith(rangeParams, RANGE_PARAM, option.range)}
              active={option.range === range}
              label={t(option.labelKey)}
            />
          ))}
        </nav>
      </div>

      <Card className="flex flex-col gap-4">
        <nav
          className="flex flex-wrap items-center gap-1"
          aria-label={t('dashboard.admin.overview.chart.metricAria')}
        >
          {REVENUE_METRICS.map((option) => (
            <Pill
              key={option.key}
              href={hrefWith(metricParams, METRIC_PARAM, option.key)}
              active={option.key === metric.key}
              label={t(option.labelKey)}
            />
          ))}
        </nav>

        {series === null ? (
          <p className="py-12 text-center text-sm text-zinc-400">
            {t('dashboard.admin.overview.chart.unavailable')}
          </p>
        ) : (
          <>
            <p className="text-sm text-zinc-500">{t(metric.captionKey)}</p>
            <RevenueChart
              points={series.points.map((point) => ({
                bucketStart: point.bucketStart,
                value: metric.select(point),
              }))}
              valueKind={metric.valueKind}
              granularity={series.granularity}
              timeZoneId={series.timeZoneId}
              currency={series.currency}
              ariaLabel={t('dashboard.admin.overview.chart.canvasAria')}
            />
            <SeriesTable series={series} metric={metric} />
          </>
        )}
      </Card>
    </section>
  );
}

interface PillProps {
  readonly href: string;
  readonly active: boolean;
  readonly label: string;
}

function Pill({ href, active, label }: PillProps) {
  return (
    <Link
      href={href}
      scroll={false}
      aria-current={active ? 'true' : undefined}
      className={`rounded-lg border px-3 py-1.5 text-sm font-medium whitespace-nowrap transition-colors ${
        active
          ? 'border-brand-500/40 bg-tint-brand-strong text-on-tint-brand'
          : 'border-transparent text-zinc-400 hover:border-zinc-700 hover:bg-zinc-800/50 hover:text-zinc-100'
      }`}
    >
      {label}
    </Link>
  );
}

interface SeriesTableProps {
  readonly series: PlatformRevenueSeries;
  readonly metric: RevenueMetric;
}

/**
 * The chart's values as a table. `sr-only` rather than absent: the canvas
 * carries no text at all, and a tooltip that is the only route to a number
 * gates it behind a pointer.
 */
function SeriesTable({ series, metric }: SeriesTableProps) {
  return (
    <table className="sr-only">
      <caption>{t('dashboard.admin.overview.chart.tableCaption')}</caption>
      <thead>
        <tr>
          <th scope="col">{t('dashboard.admin.overview.chart.tablePeriod')}</th>
          <th scope="col">{t(metric.labelKey)}</th>
        </tr>
      </thead>
      <tbody>
        {series.points.map((point) => {
          const value = metric.select(point);
          return (
            <tr key={point.bucketStart}>
              <th scope="row">
                {formatBucketPeriod(point.bucketStart, series.granularity, series.timeZoneId)}
              </th>
              <td>
                {metric.valueKind === 'money'
                  ? formatCzk(value, series.currency)
                  : String(value)}
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
