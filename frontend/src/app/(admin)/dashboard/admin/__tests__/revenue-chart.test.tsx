import { render, screen, within } from '@testing-library/react';
import { axe } from 'jest-axe';
import { describe, expect, it, vi } from 'vitest';
import {
  RevenueBucketGranularity,
  RevenueRange,
  type PlatformRevenueSeries,
} from '@/lib/api-client-helpers/admin-ops-client';
import {
  formatBucketLabel,
  formatBucketPeriod,
  formatReportingMonth,
  parseMonthParam,
  shiftMonth,
  toMonthParam,
} from '@/lib/format/reporting-period';
import { RevenueChartPanel } from '../revenue-chart-panel';
import {
  METRIC_PARAM,
  RANGE_PARAM,
  REVENUE_METRICS,
  parseRevenueMetric,
  parseRevenueRange,
} from '../revenue-metrics';

/**
 * T-0192 revenue chart. The canvas itself is not testable in jsdom (Chart.js
 * needs a real 2D context), and asserting that a library drew a line would be
 * testing the library. What IS worth pinning is everything around it, which
 * is where this feature can silently lie:
 *
 * - the URL parsers, which stand between a hand-typed param and a 400;
 * - the measure projection, so the chart plots the number its label claims;
 * - the accessible table, which is the only route to a value without a
 *   pointer — a canvas is opaque to a screen reader;
 * - the period labels, which must be formatted in the timezone the buckets
 *   were computed in, never the browser's.
 */

// The canvas is replaced wholesale: jsdom has no 2D context, and the panel's
// contract with it is just the props it passes down.
vi.mock('../revenue-chart', () => ({
  RevenueChart: (props: { points: readonly { bucketStart: string; value: number }[] }) => (
    <div data-testid="chart" data-values={props.points.map((p) => p.value).join(',')} />
  ),
}));

const PRAGUE = 'Europe/Prague';

function series(overrides: Partial<PlatformRevenueSeries> = {}): PlatformRevenueSeries {
  return {
    range: RevenueRange.Week,
    granularity: RevenueBucketGranularity.Day,
    fromInclusive: '2026-08-15T22:00:00+00:00',
    toExclusive: '2026-08-22T14:30:00+00:00',
    currency: 'CZK',
    timeZoneId: PRAGUE,
    points: [
      {
        bucketStart: '2026-08-15T22:00:00+00:00',
        paidOrderCount: 2,
        grossVolumeMinor: 115_800,
        platformFeeMinor: 15_000,
        makerPayoutMinor: 100_800,
        refundedMinor: 0,
      },
      {
        bucketStart: '2026-08-16T22:00:00+00:00',
        paidOrderCount: 0,
        grossVolumeMinor: 0,
        platformFeeMinor: 0,
        makerPayoutMinor: 0,
        refundedMinor: 0,
      },
      {
        bucketStart: '2026-08-17T22:00:00+00:00',
        paidOrderCount: 1,
        grossVolumeMinor: 57_900,
        platformFeeMinor: 7_500,
        makerPayoutMinor: 50_400,
        refundedMinor: 57_900,
      },
    ],
    ...overrides,
  };
}

const fee = REVENUE_METRICS[0];

describe('parseRevenueRange', () => {
  it('reads every span the UI offers', () => {
    expect(parseRevenueRange('Day')).toBe(RevenueRange.Day);
    expect(parseRevenueRange('Week')).toBe(RevenueRange.Week);
    expect(parseRevenueRange('Month')).toBe(RevenueRange.Month);
    expect(parseRevenueRange('Quarter')).toBe(RevenueRange.Quarter);
    expect(parseRevenueRange('HalfYear')).toBe(RevenueRange.HalfYear);
    expect(parseRevenueRange('Year')).toBe(RevenueRange.Year);
  });

  it('falls back to 30 days for junk instead of reaching the API', () => {
    // The backend Validator would 400 and blank the chart; a typo is not a
    // request for an empty panel.
    expect(parseRevenueRange('Decade')).toBe(RevenueRange.Month);
    expect(parseRevenueRange('')).toBe(RevenueRange.Month);
  });
});

describe('parseRevenueMetric', () => {
  it('reads every measure the UI offers', () => {
    for (const metric of REVENUE_METRICS) {
      expect(parseRevenueMetric(metric.key).key).toBe(metric.key);
    }
  });

  it('falls back to the commission, which the panel above is already answering', () => {
    expect(parseRevenueMetric('profit').key).toBe('fee');
    expect(parseRevenueMetric('').key).toBe('fee');
  });

  it('projects each measure off the right field', () => {
    const point = series().points[2];
    const value = (key: string) => parseRevenueMetric(key).select(point);

    expect(value('fee')).toBe(7_500);
    expect(value('gross')).toBe(57_900);
    expect(value('payout')).toBe(50_400);
    expect(value('refunded')).toBe(57_900);
    expect(value('orders')).toBe(1);
  });

  it('marks the order count as a count, so it is never formatted as money', () => {
    expect(parseRevenueMetric('orders').valueKind).toBe('count');
    expect(parseRevenueMetric('fee').valueKind).toBe('money');
  });
});

describe('reporting-period formatting', () => {
  it('names a month in the Czech nominative', () => {
    expect(formatReportingMonth(2026, 8)).toBe('srpen 2026');
    expect(formatReportingMonth(2026, 1)).toBe('leden 2026');
  });

  it('labels a bucket in the timezone it was computed in, never the browser one', () => {
    // 22:00 UTC on 15 August IS 16 August in Prague. A browser formatting in
    // its own zone would draw an axis that disagreed with its own data.
    const bucket = '2026-08-15T22:00:00+00:00';

    expect(formatBucketLabel(bucket, RevenueBucketGranularity.Day, PRAGUE)).toBe('16. 8.');
    expect(formatBucketLabel(bucket, RevenueBucketGranularity.Day, 'UTC')).toBe('15. 8.');
  });

  it('labels each granularity at the resolution it carries', () => {
    const bucket = '2026-08-22T12:00:00+00:00';

    expect(formatBucketLabel(bucket, RevenueBucketGranularity.Hour, PRAGUE)).toBe('14:00');
    expect(formatBucketLabel(bucket, RevenueBucketGranularity.Day, PRAGUE)).toBe('22. 8.');
    // No year on a monthly axis: a twelve-month window holds each month once,
    // and cs-CZ widens `short` back to the full name as soon as a year is
    // present. The full period stays in the tooltip and the table.
    expect(formatBucketLabel(bucket, RevenueBucketGranularity.Month, PRAGUE)).toBe('srp');
  });

  it('says which PERIOD a bucket covers, not just when it opened', () => {
    // "17. 8." alone is ambiguous on a weekly series, where it means the
    // whole week.
    const bucket = '2026-08-16T22:00:00+00:00';

    expect(formatBucketPeriod(bucket, RevenueBucketGranularity.Week, PRAGUE)).toBe(
      'týden od 17. 8. 2026',
    );
    expect(formatBucketPeriod(bucket, RevenueBucketGranularity.Hour, PRAGUE)).toContain('–');
    expect(formatBucketPeriod(bucket, RevenueBucketGranularity.Month, PRAGUE)).toBe('srpen 2026');
  });

  it('round-trips a month through the URL param', () => {
    expect(toMonthParam(2026, 8)).toBe('2026-08');
    expect(parseMonthParam('2026-08')).toEqual({ year: 2026, month: 8 });
  });

  it('rejects a hand-typed month rather than passing it to a 400', () => {
    expect(parseMonthParam('2026-13')).toBeNull();
    expect(parseMonthParam('2026-00')).toBeNull();
    expect(parseMonthParam('1999-05')).toBeNull();
    expect(parseMonthParam('srpen')).toBeNull();
    expect(parseMonthParam('')).toBeNull();
  });

  it('rolls the year when stepping past a boundary', () => {
    expect(shiftMonth(2026, 1, -1)).toEqual({ year: 2025, month: 12 });
    expect(shiftMonth(2026, 12, 1)).toEqual({ year: 2027, month: 1 });
    expect(shiftMonth(2026, 6, -12)).toEqual({ year: 2025, month: 6 });
  });
});

describe('RevenueChartPanel', () => {
  it('plots the measure its label claims', () => {
    render(<RevenueChartPanel series={series()} range={RevenueRange.Week} metric={fee} />);

    expect(screen.getByTestId('chart')).toHaveAttribute('data-values', '15000,0,7500');
  });

  it('plots a different field when the measure changes', () => {
    render(
      <RevenueChartPanel
        series={series()}
        range={RevenueRange.Week}
        metric={parseRevenueMetric('orders')}
      />,
    );

    expect(screen.getByTestId('chart')).toHaveAttribute('data-values', '2,0,1');
  });

  it('drives both controls through the URL, marking the chosen ones', () => {
    render(<RevenueChartPanel series={series()} range={RevenueRange.Week} metric={fee} />);

    const ranges = screen.getByRole('navigation', { name: 'Rozsah grafu' });
    expect(within(ranges).getByRole('link', { name: '1 rok' })).toHaveAttribute(
      'href',
      `?${METRIC_PARAM}=fee&${RANGE_PARAM}=Year`,
    );
    const currentRange = within(ranges)
      .getAllByRole('link')
      .filter((l) => l.getAttribute('aria-current') === 'true');
    expect(currentRange).toHaveLength(1);
    expect(currentRange[0]).toHaveAccessibleName('7 dní');

    const metrics = screen.getByRole('navigation', { name: 'Zobrazená veličina' });
    expect(within(metrics).getByRole('link', { name: 'Obrat' })).toHaveAttribute(
      'href',
      `?${RANGE_PARAM}=Week&${METRIC_PARAM}=gross`,
    );
  });

  it('offers every span from a single day to a full year', () => {
    render(<RevenueChartPanel series={series()} range={RevenueRange.Week} metric={fee} />);

    const ranges = screen.getByRole('navigation', { name: 'Rozsah grafu' });
    expect(within(ranges).getAllByRole('link').map((l) => l.textContent)).toEqual([
      '1 den',
      '7 dní',
      '30 dní',
      '3 měsíce',
      '6 měsíců',
      '1 rok',
    ]);
  });

  it('carries the chosen month across a range change', () => {
    render(
      <RevenueChartPanel
        series={series()}
        range={RevenueRange.Week}
        metric={fee}
        extraParams={{ month: '2026-07' }}
      />,
    );

    expect(screen.getByRole('link', { name: '1 rok' }).getAttribute('href')).toContain(
      'month=2026-07',
    );
  });

  it('publishes every plotted value as a real table, not only as a tooltip', () => {
    // The canvas carries no text at all; a value reachable only by hovering
    // is a value gated behind a pointer.
    render(<RevenueChartPanel series={series()} range={RevenueRange.Week} metric={fee} />);

    const table = screen.getByRole('table', { name: 'Hodnoty z grafu vývoje tržeb' });
    expect(within(table).getAllByRole('row')).toHaveLength(4); // header + 3 buckets
    expect(within(table).getByText('150 Kč')).toBeInTheDocument();
    expect(within(table).getByText('75 Kč')).toBeInTheDocument();
    // The empty day is published as zero, not omitted.
    expect(within(table).getByText('0 Kč')).toBeInTheDocument();
  });

  it('labels the table rows by period, in the buckets own timezone', () => {
    render(<RevenueChartPanel series={series()} range={RevenueRange.Week} metric={fee} />);

    const table = screen.getByRole('table', { name: 'Hodnoty z grafu vývoje tržeb' });
    expect(within(table).getByText(/16\. 8\. 2026/)).toBeInTheDocument();
  });

  it('says the read failed rather than drawing a flat line at zero', () => {
    render(<RevenueChartPanel series={null} range={RevenueRange.Week} metric={fee} />);

    expect(screen.getByText('Graf se nepodařilo načíst.')).toBeInTheDocument();
    expect(screen.queryByTestId('chart')).not.toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    const { container } = render(
      <RevenueChartPanel series={series()} range={RevenueRange.Week} metric={fee} />,
    );

    expect(await axe(container)).toHaveNoViolations();
  });
});
