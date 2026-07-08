'use client';

import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Select } from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import {
  DisputeCategory,
  openCustomerDispute,
  OrderState,
} from '@/lib/api-client-helpers/orders-client';
import { type MessageKey, t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * T-0145 "Eskalovat na Makables" — renders BELOW the order-message
 * thread on the order-detail page. Per the ticket's UX-ordering rule
 * (Scope §2), "Reklamovat" no longer opens a standalone dispute form:
 * the customer talks to the maker in the thread first, and only this
 * explicit action calls `OpenCustomerDispute.Command`.
 *
 * <para>
 * The 14-day-window visibility check below is presentational ONLY — a
 * UI affordance to avoid an inevitable failed submit once the window is
 * obviously closed. The backend (`order.dispute.windowExpired`) is the
 * sole authority; if the client's clock or this calculation ever
 * disagrees with the server, the POST still 409s and the error surfaces
 * via `resolveErrorMessage`. No pricing/state-machine logic lives here
 * (CLAUDE.md "no business logic on the frontend").
 * </para>
 */

const OPEN_WINDOW_DAYS = 14;

/** Customer-selectable dispute categories (§C.6 — the two carrier-reserved values are never offered here). */
type CustomerDisputeCategory = Exclude<
  DisputeCategory,
  DisputeCategory.CarrierReturned | DisputeCategory.CarrierFailed
>;

const CATEGORY_VALUES: readonly CustomerDisputeCategory[] = [
  DisputeCategory.NotDelivered,
  DisputeCategory.DamagedItem,
  DisputeCategory.NotAsDescribed,
  DisputeCategory.Other,
];

const CATEGORY_LABEL_KEYS: Record<CustomerDisputeCategory, MessageKey> = {
  [DisputeCategory.NotDelivered]: 'customer.orderDetail.dispute.category.notDelivered',
  [DisputeCategory.DamagedItem]: 'customer.orderDetail.dispute.category.damagedItem',
  [DisputeCategory.NotAsDescribed]: 'customer.orderDetail.dispute.category.notAsDescribed',
  [DisputeCategory.Other]: 'customer.orderDetail.dispute.category.other',
};

/**
 * Presentational mirror of the backend's 14-day-from-delivery guard
 * (`OpenCustomerDispute.OpenWindowDays`). Returns `true` when there is no
 * `deliveredAt` yet (AC-3 — nothing to anchor a window to).
 */
function isWithinOpenWindow(deliveredAt: string | undefined, now: Date): boolean {
  if (!deliveredAt) return true;
  const delivered = new Date(deliveredAt);
  if (Number.isNaN(delivered.getTime())) return true;
  const deadline = new Date(delivered);
  deadline.setUTCDate(deadline.getUTCDate() + OPEN_WINDOW_DAYS);
  return now.getTime() <= deadline.getTime();
}

interface DisputeEscalationClientProps {
  readonly orderId: string;
  readonly state: OrderState;
  readonly deliveredAt: string | undefined;
}

export function DisputeEscalationClient({
  orderId,
  state,
  deliveredAt,
}: DisputeEscalationClientProps) {
  const router = useRouter();
  const [category, setCategory] = useState<string>(DisputeCategory.NotDelivered);
  const [description, setDescription] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Already disputed — the thread + the admin resolve flow own the rest
  // of the lifecycle; no escalate action to show.
  if (state === OrderState.Disputed) {
    return <p className="text-sm text-zinc-400">{t('customer.orderDetail.dispute.alreadyOpenNote')}</p>;
  }

  const windowOpen = isWithinOpenWindow(deliveredAt, new Date());
  if (!windowOpen) {
    return <p className="text-sm text-zinc-400">{t('customer.orderDetail.dispute.windowExpiredNote')}</p>;
  }

  const descriptionValid = description.trim() !== '';
  const canSubmit = descriptionValid && category !== '' && !submitting;

  async function handleSubmit() {
    if (!canSubmit) return;
    setSubmitting(true);
    setError(null);

    const result = await openCustomerDispute(
      orderId,
      category as DisputeCategory,
      description.trim(),
    );

    if (result.success) {
      router.refresh();
      return;
    }

    setError(resolveErrorMessage(result.error));
    setSubmitting(false);
  }

  return (
    <div className="flex flex-col gap-4 border-t border-zinc-800 pt-4">
      <div>
        <h3 className="text-sm font-semibold text-zinc-200">
          {t('customer.orderDetail.dispute.heading')}
        </h3>
        <p className="mt-1 text-sm text-zinc-400">{t('customer.orderDetail.dispute.intro')}</p>
      </div>

      {error ? <Alert variant="error">{error}</Alert> : null}

      <Select
        label={t('customer.orderDetail.dispute.categoryLabel')}
        value={category}
        onChange={(event) => setCategory(event.target.value)}
        disabled={submitting}
        options={CATEGORY_VALUES.map((c) => ({ value: c, label: t(CATEGORY_LABEL_KEYS[c]) }))}
      />

      <Textarea
        rows={3}
        label={t('customer.orderDetail.dispute.descriptionLabel')}
        value={description}
        onChange={(event) => setDescription(event.target.value)}
        disabled={submitting}
      />

      <div className="flex items-center justify-end">
        <Button
          type="button"
          variant="outline"
          loading={submitting}
          disabled={!canSubmit}
          onClick={() => void handleSubmit()}
        >
          {submitting
            ? t('customer.orderDetail.dispute.escalateSubmitting')
            : t('customer.orderDetail.dispute.escalateButton')}
        </Button>
      </div>
    </div>
  );
}
