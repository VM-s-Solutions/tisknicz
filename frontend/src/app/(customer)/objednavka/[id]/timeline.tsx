import { Icon } from '@/components/ui/icon';
import type { CustomerOrderDetail } from '@/lib/api-client-helpers/orders-client';
import { t } from '@/lib/i18n';
import { formatDateTime } from '@/lib/utils/dates';

/**
 * Vertical lifecycle timeline (T-0086b §C) — pure presentation over the
 * five nullable T-0082 timestamps. Completed steps render a teal check
 * in a ring, the active (latest filled) step renders a solid teal dot,
 * future steps render muted zinc circles. When `cancelledAt` is
 * non-null the remaining steps are replaced by a terminal "Zrušeno"
 * node. `Completed`/`Refunded`/`Disputed` carry no dedicated timestamps
 * — the header state badge represents them; the timeline simply ends at
 * its last filled node. No state machine here — display lookup only.
 */

interface TimelineStep {
  readonly label: string;
  readonly timestamp: string | undefined;
  readonly cancelled?: boolean;
}

/**
 * Wire-honest presence check: the generated types declare the optional
 * timestamps as `string | undefined`, but ASP.NET serialises absent
 * values as JSON `null` — `typeof` covers both shapes without loose
 * equality.
 */
function hasTimestamp(value: string | undefined): value is string {
  return typeof value === 'string' && value !== '';
}

export function OrderTimeline({ detail }: { readonly detail: CustomerOrderDetail }) {
  const lifecycle: readonly TimelineStep[] = [
    { label: t('customer.orderDetail.timeline.created'), timestamp: detail.createdAt },
    { label: t('customer.orderDetail.timeline.paid'), timestamp: detail.paidAt },
    { label: t('customer.orderDetail.timeline.accepted'), timestamp: detail.acceptedAt },
    { label: t('customer.orderDetail.timeline.shipped'), timestamp: detail.shippedAt },
    { label: t('customer.orderDetail.timeline.delivered'), timestamp: detail.deliveredAt },
  ];

  // Cancelled branch: keep the steps that actually happened, then a
  // terminal Zrušeno node — no muted future steps after a cancellation.
  const steps: readonly TimelineStep[] = hasTimestamp(detail.cancelledAt)
    ? [
        ...lifecycle.filter((step) => hasTimestamp(step.timestamp)),
        {
          label: t('customer.orderDetail.timeline.cancelled'),
          timestamp: detail.cancelledAt,
          cancelled: true,
        },
      ]
    : lifecycle;

  // Display-only: the latest filled, non-cancelled step is "active".
  const activeIndex = steps.reduce(
    (acc, step, index) => (hasTimestamp(step.timestamp) && !step.cancelled ? index : acc),
    -1,
  );

  return (
    <div className="flex flex-col gap-4">
      <h2 className="flex items-center gap-2 text-xs font-semibold tracking-widest text-zinc-500 uppercase">
        <Icon name="clock" size={14} className="shrink-0" />
        {t('customer.orderDetail.timeline.heading')}
      </h2>
      <ol className="flex flex-col">
        {steps.map((step, index) => (
          <TimelineNode
            key={step.label}
            step={step}
            isActive={index === activeIndex}
            connectorFilled={
              index + 1 < steps.length && hasTimestamp(steps[index + 1].timestamp)
            }
            isLast={index === steps.length - 1}
          />
        ))}
      </ol>
    </div>
  );
}

function TimelineNode({
  step,
  isActive,
  connectorFilled,
  isLast,
}: {
  readonly step: TimelineStep;
  readonly isActive: boolean;
  readonly connectorFilled: boolean;
  readonly isLast: boolean;
}) {
  const filled = hasTimestamp(step.timestamp);

  const dotClass = step.cancelled
    ? 'border-error/40 bg-error/10 text-error'
    : isActive
      ? 'border-brand-400 bg-brand-400 text-on-brand'
      : filled
        ? 'border-brand-400/40 bg-brand-400/10 text-brand-400'
        : 'border-zinc-800 bg-zinc-900 text-zinc-500';

  return (
    <li className="flex gap-4">
      <div className="flex flex-col items-center">
        <span
          className={`flex h-6 w-6 shrink-0 items-center justify-center rounded-full border ${dotClass}`}
          aria-hidden="true"
        >
          {step.cancelled ? (
            <Icon name="x" size={12} />
          ) : filled ? (
            <Icon name="check" size={12} />
          ) : null}
        </span>
        {!isLast ? (
          <span
            className={`w-px grow ${connectorFilled ? 'bg-brand-400/40' : 'bg-zinc-800'}`}
            aria-hidden="true"
          />
        ) : null}
      </div>
      <div className={`flex flex-col gap-0.5 pt-0.5 ${isLast ? 'pb-0' : 'pb-6'}`}>
        <span
          className={`text-sm font-semibold ${
            step.cancelled ? 'text-error' : filled ? 'text-zinc-100' : 'text-zinc-500'
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
