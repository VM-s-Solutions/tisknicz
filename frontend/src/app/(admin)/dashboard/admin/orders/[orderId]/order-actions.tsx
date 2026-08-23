'use client';

import { useRouter } from 'next/navigation';
import { useEffect, useId, useRef, useState, useTransition } from 'react';
import { Alert } from '@/components/ui/alert';
import { Dialog } from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Dropdown } from '@/components/ui/dropdown';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import {
  changeOrderState,
  OrderState,
  refundOrder,
} from '@/lib/api-client-helpers/admin-orders';
import { t } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { orderStateLabelKey } from '@/lib/orders/state-labels';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * Money/state admin action surfaces for the order detail (T-0118b §A.1 +
 * §A.2). Two MODAL triggers (refund, manual state change), each with the
 * money-grade disabled-button-while-pending idempotency lock (T-0087b
 * ship-confirm precedent extended to a money path). The UI implements NO
 * business rules: the refund gate, the remaining-refundable cap and the
 * manual-transition allow-list are all backend — these surfaces post the
 * entered body and render the backend's verdict via `resolveErrorMessage`,
 * then `router.refresh()` so the Server Component reconciles against the
 * real row (no optimistic UI).
 */

const REASON_MIN_LENGTH = 10;

interface OrderActionsProps {
  readonly orderId: string;
  readonly orderNumber: string;
  readonly state: OrderState;
  readonly totalAmountMinor: number;
  readonly currency: string;
}

export function OrderActions({
  orderId,
  orderNumber,
  state,
  totalAmountMinor,
  currency,
}: OrderActionsProps) {
  const [refundOpen, setRefundOpen] = useState(false);
  const [stateOpen, setStateOpen] = useState(false);
  // T-0176 (audit ADM-M6): the highest-stakes admin actions — a refund
  // and a manual state change — used to end with the modal simply
  // closing. A partial refund may not even change the state badge, so
  // the only signal was a breakdown row quietly differing. The success
  // notice lives on the PAGE (the modal that would have shown it is
  // gone by then) and survives the router.refresh().
  const [successNotice, setSuccessNotice] = useState<string | null>(null);

  return (
    <div className="flex flex-col gap-3">
      {successNotice ? <Alert variant="success">{successNotice}</Alert> : null}
      <div className="flex flex-wrap items-center gap-3">
      <Button type="button" variant="danger" size="lg" onClick={() => setRefundOpen(true)}>
        <Icon name="creditCard" size={16} />
        {t('dashboard.admin.orderActions.refund.trigger')}
      </Button>
      <Button type="button" variant="secondary" size="lg" onClick={() => setStateOpen(true)}>
        <Icon name="edit" size={16} />
        {t('dashboard.admin.orderActions.state.trigger')}
      </Button>

      {refundOpen ? (
        <RefundModal
          orderId={orderId}
          orderNumber={orderNumber}
          state={state}
          totalAmountMinor={totalAmountMinor}
          currency={currency}
          onClose={() => setRefundOpen(false)}
          onRefunded={(message) => setSuccessNotice(message)}
        />
      ) : null}

      {stateOpen ? (
        <StateChangeModal
          orderId={orderId}
          orderNumber={orderNumber}
          currentState={state}
          onClose={() => setStateOpen(false)}
          onChanged={(message) => setSuccessNotice(message)}
        />
      ) : null}
      </div>
    </div>
  );
}

function RefundModal({
  orderId,
  orderNumber,
  state,
  totalAmountMinor,
  currency,
  onClose,
  onRefunded,
}: {
  readonly orderId: string;
  readonly orderNumber: string;
  readonly state: OrderState;
  readonly totalAmountMinor: number;
  readonly currency: string;
  readonly onClose: () => void;
  readonly onRefunded: (message: string) => void;
}) {
  const router = useRouter();
  const titleId = useId();
  // amountMinor defaults to the full total; entered in whole CZK and
  // scaled to minor units at submit (display scaling only — the
  // remaining-refundable cap is backend math, A.1).
  const [amountWhole, setAmountWhole] = useState(String(Math.trunc(totalAmountMinor / 100)));
  const [reason, setReason] = useState('');
  const [acknowledge, setAcknowledge] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isRefreshing, startTransition] = useTransition();
  // The money-grade idempotency lock: a second click cannot fire while a
  // POST is in flight OR while the post-success refresh is settling (A.1,
  // no backend partial-refund idempotency key — Q-0018).
  const inFlightRef = useRef(false);
  const [submitting, setSubmitting] = useState(false);

  const requiresAck = state === OrderState.Completed;
  const parsedWhole = Number.parseInt(amountWhole, 10);
  const amountValid = Number.isFinite(parsedWhole) && parsedWhole > 0;
  const reasonValid = reason.trim() !== '';
  const ackValid = !requiresAck || acknowledge;
  const busy = submitting || isRefreshing;
  const canSubmit = amountValid && reasonValid && ackValid && !busy;

  async function handleSubmit() {
    if (inFlightRef.current || !canSubmit) return;
    inFlightRef.current = true;
    setSubmitting(true);
    setError(null);

    const result = await refundOrder(orderId, {
      amountMinor: parsedWhole * 100,
      reason: reason.trim(),
      acknowledgePostPayout: acknowledge,
    });

    if (result.success) {
      startTransition(() => {
        router.refresh();
      });
      onRefunded(
        t('dashboard.admin.orderActions.refund.success', {
          amount: formatCzk(parsedWhole * 100, currency),
          orderNumber,
        }),
      );
      onClose();
      return;
    }

    setError(resolveErrorMessage(result.error));
    inFlightRef.current = false;
    setSubmitting(false);
  }

  return (
    <Dialog
      titleId={titleId}
      title={t('dashboard.admin.orderActions.refund.title', { orderNumber })}
      onClose={onClose}
      closeDisabled={busy}
    >
      <p className="text-sm text-zinc-400">{t('dashboard.admin.orderActions.refund.intro')}</p>
      <Alert variant="warning">
        <p>{t('dashboard.admin.orderActions.refund.irreversibleNote')}</p>
      </Alert>

      {error ? <Alert variant="error">{error}</Alert> : null}

      <Input
        type="number"
        inputMode="numeric"
        min={1}
        label={t('dashboard.admin.orderActions.refund.amountLabel')}
        value={amountWhole}
        onChange={(event) => setAmountWhole(event.target.value)}
        disabled={busy}
      />
      <p className="-mt-2 text-xs text-zinc-500">
        {t('dashboard.admin.orderActions.refund.amountHint', {
          total: formatCzk(totalAmountMinor, currency),
        })}
      </p>

      <Textarea
        rows={3}
        label={t('dashboard.admin.orderActions.refund.reasonLabel')}
        value={reason}
        onChange={(event) => setReason(event.target.value)}
        disabled={busy}
      />

      {requiresAck ? (
        <div className="rounded-xl border border-warning/40 bg-warning/10 p-3">
          <Checkbox
            checked={acknowledge}
            onChange={(event) => setAcknowledge(event.target.checked)}
            disabled={busy}
            label={
              <span className="text-warning">
                {t('dashboard.admin.orderActions.refund.postPayoutAck')}
              </span>
            }
          />
        </div>
      ) : null}

      <div className="mt-2 flex items-center justify-end gap-3">
        <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
          {t('common.cancel')}
        </Button>
        <Button
          type="button"
          variant="danger"
          loading={busy}
          disabled={!canSubmit}
          onClick={() => void handleSubmit()}
        >
          {busy
            ? t('dashboard.admin.orderActions.refund.submitting')
            : t('dashboard.admin.orderActions.refund.submit')}
        </Button>
      </div>
    </Dialog>
  );
}

// All states offered as candidate targets minus the current one — the
// allow-list is BACKEND-enforced (ManualOrderTransitionPolicy); the UI
// must not re-implement it (A.2, forbidden business logic).
const ALL_ORDER_STATES: readonly OrderState[] = [
  OrderState.PendingPayment,
  OrderState.Paid,
  OrderState.Accepted,
  OrderState.Shipped,
  OrderState.Delivered,
  OrderState.Completed,
  OrderState.Cancelled,
  OrderState.Refunded,
  OrderState.Disputed,
];

function StateChangeModal({
  orderId,
  orderNumber,
  currentState,
  onClose,
  onChanged,
}: {
  readonly orderId: string;
  readonly orderNumber: string;
  readonly currentState: OrderState;
  readonly onClose: () => void;
  readonly onChanged: (message: string) => void;
}) {
  const router = useRouter();
  const titleId = useId();
  const candidates = ALL_ORDER_STATES.filter((s) => s !== currentState);
  const [targetState, setTargetState] = useState<string>(candidates[0] ?? '');
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isRefreshing, startTransition] = useTransition();
  const inFlightRef = useRef(false);
  const [submitting, setSubmitting] = useState(false);

  const reasonTrimmed = reason.trim();
  // UI mirrors only the length HINT — the backend Validator
  // (MinimumLength(10)) stays authoritative (A.2).
  const reasonValid = reasonTrimmed.length >= REASON_MIN_LENGTH;
  const targetValid = targetState !== '';
  const busy = submitting || isRefreshing;
  const canSubmit = reasonValid && targetValid && !busy;

  async function handleSubmit() {
    if (inFlightRef.current || !canSubmit) return;
    inFlightRef.current = true;
    setSubmitting(true);
    setError(null);

    const result = await changeOrderState(orderId, {
      targetState: targetState as OrderState,
      reason: reasonTrimmed,
    });

    if (result.success) {
      startTransition(() => {
        router.refresh();
      });
      onChanged(
        t('dashboard.admin.orderActions.state.success', {
          orderNumber,
          state: t(orderStateLabelKey(targetState as OrderState)),
        }),
      );
      onClose();
      return;
    }

    setError(resolveErrorMessage(result.error));
    inFlightRef.current = false;
    setSubmitting(false);
  }

  return (
    <Dialog
      titleId={titleId}
      title={t('dashboard.admin.orderActions.state.title', { orderNumber })}
      onClose={onClose}
      closeDisabled={busy}
    >
      <p className="text-sm text-zinc-400">
        {t('dashboard.admin.orderActions.state.intro', {
          current: t(orderStateLabelKey(currentState)),
        })}
      </p>

      {error ? <Alert variant="error">{error}</Alert> : null}

      <Dropdown
        label={t('dashboard.admin.orderActions.state.targetLabel')}
        value={targetState}
        onChange={setTargetState}
        disabled={busy}
        options={candidates.map((s) => ({ value: s, label: t(orderStateLabelKey(s)) }))}
      />

      <Textarea
        rows={3}
        label={t('dashboard.admin.orderActions.state.reasonLabel')}
        value={reason}
        onChange={(event) => setReason(event.target.value)}
        disabled={busy}
        error={
          reasonTrimmed.length > 0 && !reasonValid
            ? t('dashboard.admin.orderActions.state.reasonHint')
            : undefined
        }
      />
      {reasonTrimmed.length === 0 ? (
        <p className="-mt-2 text-xs text-zinc-500">
          {t('dashboard.admin.orderActions.state.reasonHint')}
        </p>
      ) : null}

      <div className="mt-2 flex items-center justify-end gap-3">
        <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
          {t('common.cancel')}
        </Button>
        <Button type="button" loading={busy} disabled={!canSubmit} onClick={() => void handleSubmit()}>
          {busy
            ? t('dashboard.admin.orderActions.state.submitting')
            : t('dashboard.admin.orderActions.state.submit')}
        </Button>
      </div>
    </Dialog>
  );
}
