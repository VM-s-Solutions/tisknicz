import Link from 'next/link';
import type { Metadata } from 'next';
import { notFound, redirect } from 'next/navigation';
import { Alert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { getCustomerOrderDetail, OrderState } from '@/lib/api-client-helpers/orders-client';
import { t } from '@/lib/i18n';
import { isPaidOrLater, orderStateLabelKey } from '@/lib/orders/state-labels';
import { FailureView, SuccessView } from './confirmation-views';
import { PaymentPollClient } from './payment-poll-client';

/**
 * Payment confirmation page at /objednavka/[id]/potvrzeni — the Comgate
 * returnUrl target (T-0085, US-customer-0010). Server Component: the
 * SSR pass does the initial detail fetch and applies the decision
 * matrix below; the poller is a client leaf mounted only for the
 * still-PendingPayment optimistic path.
 *
 * Security invariant (CLAUDE.md): the `?status=` redirect param is a UX
 * hint only — failure values short-circuit to the failure frame, but
 * success is granted exclusively by the backend-read `Paid` state.
 *
 * Decision matrix (rows evaluated top-down):
 *   1. fetch NotFound                      → notFound()
 *   2. fetch Unauthorized                  → login redirect
 *   3. Paid (or later)                     → success view, no poller
 *   4. ?status= failure value              → failure view, no poller
 *   5. Cancelled | Refunded | Disputed     → state banner
 *   6. PendingPayment                      → verifying frame + poller
 */

export const metadata: Metadata = {
  title: t('checkout.confirm.metadata.title'),
};

// Always render fresh — the verdict comes from the live order state.
export const dynamic = 'force-dynamic';

/** Defensive `?status=` parsing (T-0085 §C): only these values mean failure. */
const FAILURE_STATUS_VALUES: ReadonlySet<string> = new Set([
  'cancelled',
  'cancel',
  'failed',
  'error',
]);

interface PageProps {
  readonly params: Promise<{ id: string }>;
  readonly searchParams: Promise<Record<string, string | string[] | undefined>>;
}

function readString(value: string | string[] | undefined): string {
  if (Array.isArray(value)) return value[0] ?? '';
  return value ?? '';
}

export default async function PaymentConfirmationPage({ params, searchParams }: PageProps) {
  const { id } = await params;
  const sp = await searchParams;
  const status = readString(sp.status).trim().toLowerCase();

  const result = await getCustomerOrderDetail(id);
  if (!result.success) {
    if (result.error.type === 'NotFound') {
      notFound();
    }
    if (result.error.type === 'Unauthorized') {
      // The login page serves at /login — the (auth) route group adds
      // no URL segment.
      redirect(
        `/login?redirect=${encodeURIComponent(
          `/objednavka/${encodeURIComponent(id)}/potvrzeni`,
        )}`,
      );
    }
    return (
      <section className="mx-auto flex max-w-2xl flex-col gap-6 px-4 py-16 sm:px-6 lg:px-8">
        <Alert variant="error">
          <p className="font-semibold">{t('order.page.loadError')}</p>
          <p className="mt-1">{t('order.page.loadErrorBody')}</p>
        </Alert>
        <div>
          <Link
            href={`/objednavka/${encodeURIComponent(id)}/potvrzeni`}
            className="inline-flex items-center gap-2 text-sm font-medium text-zinc-400 transition-colors hover:text-white"
          >
            {t('order.page.loadErrorRetry')}
          </Link>
        </div>
      </section>
    );
  }
  const detail = result.value;

  // Row 3 — webhook already processed (or revisiting later): success
  // immediately, zero client polling. Post-Paid states count as success
  // so a bookmark revisit days later sees the thank-you, not a poller.
  if (isPaidOrLater(detail.state)) {
    return (
      <Frame>
        <SuccessView orderId={detail.orderId} orderNumber={detail.orderNumber} />
      </Frame>
    );
  }

  // Row 4 — the gateway reported cancellation/failure: failure frame,
  // no poll. The order is still held for 24h (T-0083); retry lives on
  // the T-0084b pay button.
  if (FAILURE_STATUS_VALUES.has(status)) {
    return (
      <Frame>
        <FailureView orderId={detail.orderId} />
      </Frame>
    );
  }

  // Row 5 — terminal/unexpected states with a non-failure param.
  if (detail.state !== OrderState.PendingPayment) {
    return (
      <Frame>
        <Card padding="lg" className="flex flex-col items-center gap-4 text-center">
          <Badge variant="default">{t(orderStateLabelKey(detail.state))}</Badge>
          <p className="text-sm text-zinc-400">{t('order.page.banner.detailComing')}</p>
          <Link
            href={`/objednavka/${encodeURIComponent(detail.orderId)}`}
            className="inline-flex items-center gap-2 text-sm font-medium text-zinc-400 transition-colors hover:text-white"
          >
            {t('checkout.confirm.pendingDetailLink')}
            <Icon name="arrowRight" size={14} />
          </Link>
        </Card>
      </Frame>
    );
  }

  // Row 6 — PendingPayment + absent/unknown/non-failure status:
  // optimistic verifying frame + capped poll. Unknown-as-optimistic is
  // safe because success only ever comes from the backend state.
  return (
    <Frame>
      <PaymentPollClient orderId={detail.orderId} orderNumber={detail.orderNumber} />
    </Frame>
  );
}

function Frame({ children }: { readonly children: React.ReactNode }) {
  return (
    <section className="mx-auto flex max-w-2xl flex-col gap-6 px-4 py-16 sm:px-6 lg:px-8">
      {children}
    </section>
  );
}
