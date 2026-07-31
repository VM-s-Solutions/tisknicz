import { Icon } from '@/components/ui/icon';
import type { MakerOrderDetail } from '@/lib/api-client-helpers/maker-orders';
import { t } from '@/lib/i18n';
import { formatDateTime } from '@/lib/utils/dates';

/**
 * Maker-view lifecycle timeline (T-0087b §C — T-0086b customer timeline
 * mirrored onto the maker DTO + maker i18n keys). Pure presentation
 * over the five nullable T-0082 timestamps: reached steps render as
 * teal check circles connected by a lit line, the most recent reached
 * step carries a soft glow ring, unreached steps render muted zinc, and
 * a non-null `cancelledAt` replaces the remaining steps with a terminal
 * "Zrušeno" node. `Completed`/`Refunded`/`Disputed` carry no dedicated
 * timestamps — the header state badge represents them. No state machine
 * here — display lookup only.
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

  // Display-only: the last reached step gets the "current" glow ring.
  const currentIndex = steps.reduce(
    (acc, step, index) => (hasTimestamp(step.timestamp) ? index : acc),
    -1,
  );

  return (
    <div className="flex flex-col gap-4">
      <h2 className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
        {t('dashboard.maker.orderDetail.timeline.heading')}
      </h2>
      <ol className="flex flex-col">
        {steps.map((step, index) => (
          <TimelineNode
            key={step.label}
            step={step}
            isLast={index === steps.length - 1}
            isCurrent={index === currentIndex}
            nextReached={index + 1 < steps.length && hasTimestamp(steps[index + 1]?.timestamp)}
          />
        ))}
      </ol>
    </div>
  );
}

function TimelineNode({
  step,
  isLast,
  isCurrent,
  nextReached,
}: {
  readonly step: TimelineStep;
  readonly isLast: boolean;
  readonly isCurrent: boolean;
  readonly nextReached: boolean;
}) {
  const filled = hasTimestamp(step.timestamp);
  const circleClass = step.cancelled
    ? 'border-red-900/50 bg-red-950 text-red-400'
    : filled
      ? isCurrent
        ? 'border-brand-400/60 bg-brand-400/15 text-brand-300 ring-4 ring-brand-400/10'
        : 'border-brand-400/40 bg-brand-400/10 text-brand-400'
      : 'border-zinc-800 bg-zinc-900 text-zinc-500';

  return (
    <li className="flex gap-4">
      <div className="flex flex-col items-center">
        <span
          className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full border ${circleClass}`}
          aria-hidden="true"
        >
          {step.cancelled ? (
            <Icon name="x" size={14} />
          ) : filled ? (
            <Icon name="check" size={14} />
          ) : null}
        </span>
        {!isLast ? (
          <span
            className={`w-px grow ${nextReached ? 'bg-brand-400/30' : 'bg-zinc-800'}`}
            aria-hidden="true"
          />
        ) : null}
      </div>
      <div className={`flex flex-col gap-0.5 pt-1 ${isLast ? 'pb-0' : 'pb-6'}`}>
        <span
          className={`text-sm font-semibold ${
            step.cancelled ? 'text-red-400' : filled ? 'text-zinc-100' : 'text-zinc-500'
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
