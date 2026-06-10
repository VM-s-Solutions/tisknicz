'use client';

import { useEffect, useRef, useState } from 'react';
import { getCustomerOrderDetail, OrderState } from '@/lib/api-client-helpers/orders-client';
import { isPaidOrLater } from '@/lib/orders/state-labels';
import { CapReachedView, FailureView, SuccessView, VerifyingView } from './confirmation-views';

/**
 * Capped, visibility-aware payment-verification poller (T-0085, Q4/Q5
 * lock). Renders the optimistic verifying frame and re-reads the
 * existing order-detail endpoint every {@link POLL_INTERVAL_MS} until
 * the T-0066/67 webhook lands (`State == Paid` → success view swaps in
 * place), the order cancels (failure view), or the active-polling
 * budget {@link POLL_CAP_MS} runs out (cap-reached view; the order-paid
 * email is the fallback).
 *
 * The timer effect is the one sanctioned effect in the checkout bundle:
 * initial data was server-fetched by the page; this effect is a
 * verification *timer* re-invoking the existing B.16 helper (the
 * no-`useEffect`-fetch rule targets initial data loading — T-0085 §B +
 * Alternatives Option E).
 *
 * Visibility-pause: while `document.hidden` the interval stops and the
 * elapsed budget freezes; on return one immediate poll fires (so a
 * customer back from their banking app gets a fresh verdict instantly)
 * and the interval resumes. Success is granted ONLY by the backend
 * state — never by the `?status=` redirect param (CLAUDE.md payments
 * rule).
 */

/** Poll spacing (~3s — grooming lock; the first poll catches most webhooks). */
export const POLL_INTERVAL_MS = 3000;
/** Active-polling budget (~30s ≈ 10 attempts — grooming lock). */
export const POLL_CAP_MS = 30_000;

type PollView = 'verifying' | 'success' | 'failure' | 'capReached';

interface PaymentPollClientProps {
  readonly orderId: string;
  readonly orderNumber: string;
}

export function PaymentPollClient({ orderId, orderNumber }: PaymentPollClientProps) {
  const [view, setView] = useState<PollView>('verifying');
  // Accrues only while the document is visible (per fired tick).
  const activeElapsedMsRef = useRef(0);
  const pollInFlightRef = useRef(false);

  useEffect(() => {
    if (view !== 'verifying') return;

    let disposed = false;
    let intervalId: number | null = null;

    const poll = async () => {
      if (disposed || pollInFlightRef.current) return;
      pollInFlightRef.current = true;
      const result = await getCustomerOrderDetail(orderId);
      pollInFlightRef.current = false;
      if (disposed || !result.success) {
        // Transient poll errors are swallowed — the next tick retries;
        // the cap bounds total effort either way.
        return;
      }
      if (isPaidOrLater(result.value.state)) {
        setView('success');
        return;
      }
      if (result.value.state === OrderState.Cancelled) {
        setView('failure');
      }
    };

    const tick = () => {
      activeElapsedMsRef.current += POLL_INTERVAL_MS;
      if (activeElapsedMsRef.current >= POLL_CAP_MS) {
        setView('capReached');
        return;
      }
      void poll();
    };

    const start = () => {
      if (intervalId === null) {
        intervalId = window.setInterval(tick, POLL_INTERVAL_MS);
      }
    };
    const stop = () => {
      if (intervalId !== null) {
        window.clearInterval(intervalId);
        intervalId = null;
      }
    };

    const onVisibilityChange = () => {
      if (document.hidden) {
        // Pause: budget freezes while hidden.
        stop();
      } else {
        // One immediate poll (does not consume budget), then resume.
        void poll();
        start();
      }
    };

    document.addEventListener('visibilitychange', onVisibilityChange);
    if (!document.hidden) {
      start();
    }

    return () => {
      disposed = true;
      stop();
      document.removeEventListener('visibilitychange', onVisibilityChange);
    };
  }, [view, orderId]);

  switch (view) {
    case 'verifying':
      return <VerifyingView />;
    case 'success':
      return <SuccessView orderId={orderId} orderNumber={orderNumber} />;
    case 'failure':
      return <FailureView orderId={orderId} />;
    case 'capReached':
      return <CapReachedView orderId={orderId} />;
  }
}
