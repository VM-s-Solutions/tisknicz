'use client';

import { useRouter } from 'next/navigation';
import { useId, useRef, useState, useTransition } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { cancelPendingOrder } from '@/lib/api-client-helpers/orders-client';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * Customer cancels their own unpaid order (T-0181 / Q-0041, audit
 * CUST-M3).
 *
 * <para>
 * An accidental or abandoned order previously offered only "Zaplatit";
 * the sole exit was the silent 24 h auto-expiry, while the order sat in
 * the list as "Čeká na platbu" with no way to be rid of it. T-0172
 * shipped copy explaining the auto-cancel as an interim; this is the
 * real action.
 * </para>
 *
 * <para>
 * Confirm-gated even though no money moves — the order is gone
 * afterwards, and an accidental cancel would mean re-entering the whole
 * checkout.
 * </para>
 */
export function CancelOrderClient({ orderId }: { readonly orderId: string }) {
  const router = useRouter();
  const titleId = useId();
  const [open, setOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [, startTransition] = useTransition();
  const inFlightRef = useRef(false);

  async function handleConfirm() {
    if (inFlightRef.current) return;
    inFlightRef.current = true;
    setSubmitting(true);
    setError(null);

    const result = await cancelPendingOrder(orderId);
    if (result.success) {
      setOpen(false);
      startTransition(() => router.refresh());
      return;
    }
    setError(resolveErrorMessage(result.error));
    inFlightRef.current = false;
    setSubmitting(false);
  }

  return (
    <>
      <Button
        type="button"
        variant="ghost"
        size="sm"
        className="self-start"
        onClick={() => setOpen(true)}
      >
        {t('order.page.cancel')}
      </Button>

      {open ? (
        <Dialog
          titleId={titleId}
          title={t('order.page.cancel_title')}
          onClose={() => setOpen(false)}
          closeDisabled={submitting}
        >
          <p className="text-sm text-zinc-400">{t('order.page.cancel_intro')}</p>
          {error ? <Alert variant="error">{error}</Alert> : null}
          <div className="flex flex-wrap items-center justify-end gap-3">
            <Button
              type="button"
              variant="ghost"
              onClick={() => setOpen(false)}
              disabled={submitting}
            >
              {t('common.back')}
            </Button>
            <Button
              type="button"
              variant="danger"
              loading={submitting}
              disabled={submitting}
              onClick={() => void handleConfirm()}
            >
              {t('order.page.cancel_confirm')}
            </Button>
          </div>
        </Dialog>
      ) : null}
    </>
  );
}
