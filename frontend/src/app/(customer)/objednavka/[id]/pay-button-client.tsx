'use client';

import { useRouter } from 'next/navigation';
import { useRef, useState } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { createPaymentSession } from '@/lib/api-client-helpers/payments-client';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * Explicit "Zaplatit" CTA (T-0084b, Q3 lock — the payment session is
 * created ON CLICK, never on page load). Success performs a full
 * document navigation to the Comgate redirect URL
 * (`window.location.assign` — `router.push` is for in-app routes).
 * State-conflict codes additionally `router.refresh()` so the server
 * re-render swaps to the correct state banner (the webhook beat the
 * customer to it).
 */

const STATE_CONFLICT_CODES = new Set([
  'order.invalidStateForPayment',
  'order.paymentAlreadyCaptured',
]);

export function PayButtonClient({ orderId }: { readonly orderId: string }) {
  const router = useRouter();
  const [paying, setPaying] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inFlightRef = useRef(false);

  async function handlePay() {
    if (inFlightRef.current) return;
    inFlightRef.current = true;
    setPaying(true);
    setError(null);

    const result = await createPaymentSession(orderId);
    if (result.success) {
      // Button stays disabled until the document navigates away.
      window.location.assign(result.value.redirectUrl);
      return;
    }

    setError(resolveErrorMessage(result.error));
    if (STATE_CONFLICT_CODES.has(result.error.code)) {
      router.refresh();
    }
    inFlightRef.current = false;
    setPaying(false);
  }

  return (
    <div className="flex flex-col gap-3">
      {error ? <Alert variant="error">{error}</Alert> : null}
      <Button type="button" size="lg" loading={paying} onClick={handlePay}>
        {!paying ? (
          <span aria-hidden="true">
            <Icon name="creditCard" size={16} />
          </span>
        ) : null}
        {paying ? t('order.page.paying') : t('order.page.payCta')}
      </Button>
    </div>
  );
}
