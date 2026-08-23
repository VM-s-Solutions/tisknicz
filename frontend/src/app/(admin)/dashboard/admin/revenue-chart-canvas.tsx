'use client';

import { useEffect, useRef, useSyncExternalStore } from 'react';
import {
  CategoryScale,
  Chart,
  Filler,
  LineController,
  LineElement,
  LinearScale,
  PointElement,
  Tooltip,
  type Chart as ChartType,
  type Plugin,
} from 'chart.js';
import type { RevenueBucketGranularity } from '@/lib/api-client-helpers/admin-ops-client';
import { formatBucketLabel, formatBucketPeriod } from '@/lib/format/reporting-period';
import { formatCzk } from '@/lib/money/formatter';
import {
  readResolvedTheme,
  readServerResolvedTheme,
  subscribeToResolvedTheme,
} from '@/lib/theme/theme-store';
import type { RevenueValueKind } from './revenue-metrics';

/**
 * The revenue chart itself (T-0192) — one measure over time, drawn on a
 * canvas by Chart.js.
 *
 * <para>
 * Loaded through `next/dynamic` by `revenue-chart.tsx`, so Chart.js never
 * enters the admin bundle for operators who are only looking at the KPI
 * tiles. This file is the only place in the app that imports it, and it
 * registers ONLY the controllers a line chart needs — the barrel import
 * (`chart.js/auto`) pulls in every scale and controller ever written.
 * </para>
 *
 * <para>
 * <b>One series, one axis, always.</b> The measures differ by an order of
 * magnitude (turnover is roughly seven times the commission), so plotting
 * two of them together would need a second y-scale, and the alignment
 * between two y-scales is arbitrary — it invents a correlation that is not
 * in the data. Switching measure re-renders one line instead.
 * </para>
 *
 * <para>
 * The canvas is inert to CSS: a colour set in a stroke style is a value, not
 * a `var()` that re-resolves. So the palette is read from the active theme
 * tokens at draw time and the chart is rebuilt when `data-theme` changes —
 * the same contract `hero-scene.tsx` follows for its WebGL materials.
 * </para>
 */

Chart.register(
  LineController,
  LineElement,
  PointElement,
  LinearScale,
  CategoryScale,
  Filler,
  Tooltip,
);

/** Fallbacks for a canvas that paints before the stylesheet resolves. Dark palette. */
const TOKEN_FALLBACKS: Readonly<Record<string, string>> = {
  '--brand-400': '#2dd4bf',
  '--ink-500': '#85a0aa',
  '--ink-600': '#627880',
  '--ink-800': '#212e33',
  '--surface-card': '#121d21',
};

function readToken(name: string): string {
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return value || TOKEN_FALLBACKS[name] || '#000000';
}

/**
 * The series hue at 10% — an area wash, never a saturated block. Canvas has
 * no `color-mix`, so the hex is unpacked by hand; anything that is not a
 * six-digit hex falls back to the token as-is (an opaque fill is wrong but
 * still legible, where a dropped fill would silently change the chart).
 */
function withAlpha(hex: string, alpha: number): string {
  const match = /^#([0-9a-f]{6})$/i.exec(hex);
  if (!match) return hex;
  const value = Number.parseInt(match[1], 16);
  return `rgba(${(value >> 16) & 255}, ${(value >> 8) & 255}, ${value & 255}, ${alpha})`;
}

/**
 * The crosshair. Chart.js ships no vertical-hairline plugin, and readers aim
 * at a date, not at a 2px line — without one the tooltip appears with nothing
 * anchoring it to a position on the axis.
 *
 * <para>
 * Built as a factory so the colour rides in on a closure. The alternative —
 * a `plugins.<id>` options bag — needs a `declare module 'chart.js'`
 * augmentation to type-check, which is a lot of ceremony for one string that
 * is already in scope where the chart is created.
 * </para>
 */
function makeCrosshairPlugin(color: string): Plugin<'line'> {
  return {
    id: 'makables-crosshair',
    afterDatasetsDraw(chart: ChartType<'line'>) {
      const active = chart.getActiveElements();
      if (active.length === 0) return;

      const { ctx, chartArea } = chart;
      const { x } = active[0].element;

      ctx.save();
      ctx.beginPath();
      ctx.lineWidth = 1;
      ctx.strokeStyle = color;
      ctx.moveTo(x, chartArea.top);
      ctx.lineTo(x, chartArea.bottom);
      ctx.stroke();
      ctx.restore();
    },
  };
}

export interface RevenueChartPoint {
  readonly bucketStart: string;
  readonly value: number;
}

interface RevenueChartCanvasProps {
  readonly points: readonly RevenueChartPoint[];
  /**
   * Money or a plain count. A string, not the metric object — a function
   * cannot cross the Server/Client boundary, so the server has already
   * projected each point down to one number.
   */
  readonly valueKind: RevenueValueKind;
  readonly granularity: RevenueBucketGranularity;
  readonly timeZoneId: string;
  readonly currency: string;
  /** Read by assistive tech in place of the canvas; the table below it carries the values. */
  readonly ariaLabel: string;
}

export function RevenueChartCanvas({
  points,
  valueKind,
  granularity,
  timeZoneId,
  currency,
  ariaLabel,
}: RevenueChartCanvasProps) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  // `data-theme` is written straight to the DOM (by the pre-hydration
  // bootstrap script and by the toggle), so there is no React state to read
  // — the attribute itself is the store.
  const theme = useSyncExternalStore(
    subscribeToResolvedTheme,
    readResolvedTheme,
    readServerResolvedTheme,
  );

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const series = readToken('--brand-400');
    const surface = readToken('--surface-card');
    const muted = readToken('--ink-500');
    const grid = readToken('--ink-800');

    const isMoney = valueKind === 'money';
    const formatValue = (value: number) =>
      isMoney
        ? formatCzk(value, currency)
        : new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: 0 }).format(value);

    const chart = new Chart(canvas, {
      type: 'line',
      plugins: [makeCrosshairPlugin(readToken('--ink-600'))],
      data: {
        labels: points.map((p) => formatBucketLabel(p.bucketStart, granularity, timeZoneId)),
        datasets: [
          {
            data: points.map((p) => p.value),
            borderColor: series,
            backgroundColor: withAlpha(series, 0.1),
            borderWidth: 2,
            // Straight segments: a price chart does not interpolate between
            // readings, and smoothing would invent turnover on days that had
            // none.
            tension: 0,
            fill: true,
            pointRadius: 0,
            pointHitRadius: 24,
            pointHoverRadius: 4,
            pointBackgroundColor: series,
            // A 2px ring in the surface colour keeps the marker legible where
            // it sits on the line.
            pointHoverBorderColor: surface,
            pointHoverBorderWidth: 2,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        // Static by default (design language) — and an admin dashboard on a
        // shared 2-vCPU plan has no CPU to spend animating a line.
        animation: false,
        interaction: { mode: 'index', intersect: false },
        scales: {
          x: {
            // No vertical gridlines: at 90 daily buckets they read as hatching.
            grid: { display: false },
            border: { color: grid },
            ticks: {
              color: muted,
              maxRotation: 0,
              autoSkip: true,
              maxTicksLimit: 8,
              font: { size: 11 },
            },
          },
          y: {
            beginAtZero: true,
            grid: { color: grid, drawTicks: false },
            border: { display: false },
            ticks: {
              color: muted,
              padding: 8,
              maxTicksLimit: 6,
              font: { size: 11 },
              callback: (value) => formatValue(Number(value)),
            },
          },
        },
        plugins: {
          tooltip: {
            backgroundColor: surface,
            borderColor: grid,
            borderWidth: 1,
            titleColor: muted,
            bodyColor: readToken('--ink-500'),
            padding: 10,
            // One series, so a colour swatch would be data-weight ink doing a
            // label's job — the value leads instead.
            displayColors: false,
            callbacks: {
              title: (items) => {
                const point = points[items[0]?.dataIndex ?? 0];
                return point ? formatBucketPeriod(point.bucketStart, granularity, timeZoneId) : '';
              },
              label: (item) => formatValue(Number(item.raw)),
            },
          },
        },
      },
    });

    // Chart.js keeps a canvas registry keyed on the element; leaving a live
    // instance behind makes the next mount throw "Canvas is already in use".
    return () => chart.destroy();
  }, [points, valueKind, granularity, timeZoneId, currency, theme]);

  return (
    <div className="h-72 w-full sm:h-80">
      <canvas ref={canvasRef} role="img" aria-label={ariaLabel} />
    </div>
  );
}
