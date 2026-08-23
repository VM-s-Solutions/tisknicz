'use client';

import dynamic from 'next/dynamic';
import type { ComponentProps } from 'react';
import type { RevenueChartCanvas } from './revenue-chart-canvas';

/**
 * Keeps Chart.js out of the admin bundle until something actually draws a
 * chart (T-0192).
 *
 * <para>
 * `ssr: false` is only legal inside a Client Component in the App Router,
 * which is the whole reason this one-line boundary exists — the panel around
 * it stays a Server Component. It is also correct on its own terms: the
 * chart is a canvas, so a server render would produce an empty element, and
 * the accessible table alongside it already carries every value for readers
 * who never run the script.
 * </para>
 *
 * <para>
 * The placeholder matches the canvas box exactly, so switching range or
 * measure never jumps the page while the chunk loads.
 * </para>
 */
const Canvas = dynamic(
  () => import('./revenue-chart-canvas').then((mod) => mod.RevenueChartCanvas),
  {
    ssr: false,
    loading: () => <div className="h-72 w-full sm:h-80" aria-hidden="true" />,
  },
);

export function RevenueChart(props: ComponentProps<typeof RevenueChartCanvas>) {
  return <Canvas {...props} />;
}
