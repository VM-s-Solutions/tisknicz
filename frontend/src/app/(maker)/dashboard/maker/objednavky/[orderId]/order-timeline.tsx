import type { MakerOrderDetail } from '@/lib/api-client-helpers/maker-orders';
import { t } from '@/lib/i18n';
import { formatDateTime } from '@/lib/utils/dates';

/**
 * Maker-view lifecycle timeline (T-0087b §C — T-0086b customer timeline
 * mirrored onto the maker DTO + maker i18n keys). Pure presentation
 * over the five nullable T-0082 timestamps: reached steps render filled
 * + Czech date-time, unreached steps render muted, and a non-null
 * `cancelledAt` replaces the remaining steps with a terminal "Zrušeno"
 * node. `Completed`/`Refunded`/`Disputed` carry no dedicated timestamps
 * — the header state badge represents them. No state machine here —
 * display lookup only.
 */

interface TimelineStep {
  readonly label: string;
  readonly timestamp: string | undefined;
  readonly cancelled?: boolean;
}

/**
 * Wire-honest presence check: the generated types declare the optional
 * timestamps as `string | undefined`, but ASP.NET serialises absent
 * values as JSON `null` — `typeof` covers both shapes.
 */
function hasTimestamp(value: string | undefined): value is string {
  return typeof value === 'string' && value !== '';
}

export function OrderTimeline({ detail }: { readonly detail: MakerOrderDetail }) {
  const lifecycle: readonly TimelineStep[] = [
    { label: t('dashboard.maker.orderDetail.timeline.created'), timestamp: detail.createdAt },
    { label: t('dashboard.maker.orderDetail.timeline.paid'), timestamp: detail.paidAt },
    { label: t('dashboard.maker.orderDetail.timeline.accepted'), timestamp: detail.acceptedAt },
    { label: t('dashboard.maker.orderDetail.timeline.shipped'), timestamp: detail.shippedAt },
    { label: t('dashboard.maker.orderDetail.timeline.delivered'), timestamp: detail.deliveredAt },
  ];

  // Cancelled branch: keep the steps that actually happened, then a
  // terminal Zrušeno node — no muted future steps after a cancellation.
  const steps: readonly TimelineStep[] = hasTimestamp(detail.cancelledAt)
    ? [
        ...lifecycle.filter((step) => hasTimestamp(step.timestamp)),
        {
          label: t('dashboard.maker.orderDetail.timeline.cancelled'),
          timestamp: detail.cancelledAt,
          cancelled: true,
        },
      ]
    : lifecycle;

  return (
    <div className="flex flex-col gap-2">
      <h2 className="text-sm font-semibold text-zinc-400">
        {t('dashboard.maker.orderDetail.timeline.heading')}
      </h2>
      <ol className="flex flex-col">
        {steps.map((step, index) => (
          <TimelineNode key={step.label} step={step} isLast={index === steps.length - 1} />
        ))}
      </ol>
    </div>
  );
}

function TimelineNode({ step, isLast }: { readonly step: TimelineStep; readonly isLast: boolean }) {
  const filled = hasTimestamp(step.timestamp);
  const dotClass = step.cancelled
    ? 'border-red-900/50 bg-red-950 text-red-400'
    : filled
      ? 'border-brand-400/50 bg-brand-400/10 text-brand-400'
      : 'border-zinc-800 bg-zinc-900 text-zinc-600';

  return (
    <li className="flex gap-3">
      <div className="flex flex-col items-center">
        <span
          className={`flex h-3.5 w-3.5 shrink-0 rounded-full border-2 ${dotClass}`}
          aria-hidden="true"
        />
        {!isLast ? <span className="w-px grow bg-zinc-800" aria-hidden="true" /> : null}
      </div>
      <div className={`flex flex-col gap-0.5 ${isLast ? 'pb-0' : 'pb-5'} -mt-0.5`}>
        <span
          className={`text-sm font-semibold ${
            step.cancelled ? 'text-red-400' : filled ? 'text-zinc-100' : 'text-zinc-600'
          }`}
        >
          {step.label}
        </span>
        {hasTimestamp(step.timestamp) ? (
          <span className="text-xs text-zinc-500">{formatDateTime(step.timestamp)}</span>
        ) : null}
      </div>
    </li>
  );
}
