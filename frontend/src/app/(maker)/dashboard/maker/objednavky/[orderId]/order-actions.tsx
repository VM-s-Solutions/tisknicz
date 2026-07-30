'use client';

import { useRouter } from 'next/navigation';
import { useEffect, useRef, useState, useTransition } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import {
  acceptOrder,
  downloadMakerOrderFile,
  downloadShippingLabel,
  handOverOrder,
  OrderState,
  type AcceptOrderResult,
  type HandOverOrderResult,
  type ShipOrderResult,
  ShippingMethod,
  shipOrder,
} from '@/lib/api-client-helpers/maker-orders';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';
import type { ApiError, Result } from '@/lib/runtime/result';

/**
 * State-aware action bar for the maker order detail (T-0087b A.1 lock).
 * The buttons are a PURE render function of `state + shippingMethod` —
 * zero transition rules live here; the backend re-validates every POST
 * (`order.invalidTransition`, `shipping.methodNotEligible`) and a 200
 * re-syncs the Server Component via `router.refresh()` (Q5, no client
 * state mirroring). A stale click surfaces the backend's verdict as an
 * i18n alert AND triggers a refresh so the view reconciles (AC-7).
 *
 * Only "Odeslat" gets a confirm dialog (§C): T-0072 creates a real
 * Zásilkovna shipment + label — irreversible from the UI. Accept and
 * handover stay single-click.
 *
 * The label button (AC-8) uses the blob helper, NEVER the generated
 * `label()` method (NSwag types the file response `Promise<void>` and
 * discards the PDF — §C / Option F lock).
 */

type PendingAction = 'accept' | 'ship' | 'handover' | 'label';

interface OrderActionsProps {
  readonly orderId: string;
  readonly orderNumber: string;
  readonly state: OrderState;
  readonly shippingMethod: ShippingMethod;
  readonly shippingCarrierRef: string | undefined;
}

/**
 * Wire-honest presence check: the generated type declares
 * `string | undefined`, but ASP.NET serialises absent values as JSON
 * `null` — `typeof` covers both shapes.
 */
function hasCarrierRef(value: string | undefined): value is string {
  return typeof value === 'string' && value !== '';
}

function triggerBlobDownload(blob: Blob, filename: string): void {
  const objectUrl = URL.createObjectURL(blob);
  try {
    const anchor = document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = filename;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  } finally {
    URL.revokeObjectURL(objectUrl);
  }
}

export function OrderActions({
  orderId,
  orderNumber,
  state,
  shippingMethod,
  shippingCarrierRef,
}: OrderActionsProps) {
  const router = useRouter();
  const [pending, setPending] = useState<PendingAction | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  // Keep the bar disabled until the post-success server re-render lands
  // — prevents a double-submit window between the 200 and the refresh.
  const [isRefreshing, startTransition] = useTransition();
  const inFlightRef = useRef(false);

  // The §A.1 button matrix — render-only reads of the two DTO fields.
  const showAccept = state === OrderState.Paid;
  const showShip =
    state === OrderState.Accepted && shippingMethod === ShippingMethod.ZasilkovnaPickupPoint;
  const showHandover =
    state === OrderState.Accepted && shippingMethod === ShippingMethod.PersonalPickup;
  const showLabel =
    state === OrderState.Shipped &&
    shippingMethod === ShippingMethod.ZasilkovnaPickupPoint &&
    hasCarrierRef(shippingCarrierRef);

  const busy = pending !== null || isRefreshing;

  async function runTransition(
    action: PendingAction,
    call: () => Promise<Result<AcceptOrderResult | ShipOrderResult | HandOverOrderResult, ApiError>>,
  ): Promise<void> {
    if (inFlightRef.current) return;
    inFlightRef.current = true;
    setPending(action);
    setError(null);

    const result = await call();
    if (result.success) {
      // Server re-render swaps the badge + button matrix (Q5 lock).
      startTransition(() => {
        router.refresh();
      });
      inFlightRef.current = false;
      setPending(null);
      return;
    }

    setError(resolveErrorMessage(result.error));
    if (result.error.type === 'Conflict') {
      // Stale-button race (state changed elsewhere): the alert explains,
      // the refresh reconciles the view with the real row (AC-7).
      startTransition(() => {
        router.refresh();
      });
    }
    inFlightRef.current = false;
    setPending(null);
  }

  async function handleShipConfirmed(): Promise<void> {
    setConfirmOpen(false);
    await runTransition('ship', () => shipOrder(orderId));
  }

  async function handleLabelDownload(): Promise<void> {
    if (inFlightRef.current) return;
    inFlightRef.current = true;
    setPending('label');
    setError(null);

    const result = await downloadShippingLabel(orderId);
    if (result.success) {
      triggerBlobDownload(result.value, `stitek-${orderNumber}.pdf`);
    } else {
      // 503 carries `shipping.carrierUnavailable` — the catalog copy
      // includes the "try again in a minute" hint (AC-8).
      setError(resolveErrorMessage(result.error));
    }
    inFlightRef.current = false;
    setPending(null);
  }

  if (!showAccept && !showShip && !showHandover && !showLabel) {
    // Terminal / waiting states render no actions (AC-6) — but a failed
    // stale action may have left an error worth keeping on screen.
    return error ? <Alert variant="error">{error}</Alert> : null;
  }

  return (
    <Card variant="accent" padding="md" className="flex flex-col gap-4">
      {error ? <Alert variant="error">{error}</Alert> : null}

      <div className="flex flex-wrap items-center gap-3">
        {showAccept ? (
          <Button
            type="button"
            size="lg"
            loading={pending === 'accept'}
            disabled={busy}
            onClick={() => void runTransition('accept', () => acceptOrder(orderId))}
          >
            {pending === 'accept'
              ? t('dashboard.maker.orderDetail.action.accepting')
              : t('dashboard.maker.orderDetail.action.accept')}
          </Button>
        ) : null}

        {showShip ? (
          <Button
            type="button"
            size="lg"
            loading={pending === 'ship'}
            disabled={busy}
            onClick={() => setConfirmOpen(true)}
          >
            <Icon name="truck" size={16} />
            {pending === 'ship'
              ? t('dashboard.maker.orderDetail.action.shipping')
              : t('dashboard.maker.orderDetail.action.ship')}
          </Button>
        ) : null}

        {showHandover ? (
          <Button
            type="button"
            size="lg"
            loading={pending === 'handover'}
            disabled={busy}
            onClick={() => void runTransition('handover', () => handOverOrder(orderId))}
          >
            {pending === 'handover'
              ? t('dashboard.maker.orderDetail.action.handingOver')
              : t('dashboard.maker.orderDetail.action.handover')}
          </Button>
        ) : null}

        {showLabel ? (
          <Button
            type="button"
            variant="outline"
            size="lg"
            loading={pending === 'label'}
            disabled={busy}
            onClick={() => void handleLabelDownload()}
          >
            <Icon name="download" size={16} />
            {pending === 'label'
              ? t('dashboard.maker.orderDetail.action.downloadingLabel')
              : t('dashboard.maker.orderDetail.action.downloadLabel')}
          </Button>
        ) : null}
      </div>

      {confirmOpen ? (
        <ShipConfirmDialog
          orderId={orderId}
          onCancel={() => setConfirmOpen(false)}
          onConfirm={() => void handleShipConfirmed()}
        />
      ) : null}
    </Card>
  );
}

/**
 * Ship-only confirm dialog (T-0087b §C) — lightweight inline modal per
 * the delete-product-button precedent (no shared Dialog primitive yet;
 * `window.confirm` is off-limits). Cancel/Esc/backdrop issue NO request
 * (AC-4); the request fires only from the explicit confirm button.
 */
function ShipConfirmDialog({
  orderId,
  onCancel,
  onConfirm,
}: {
  readonly orderId: string;
  readonly onCancel: () => void;
  readonly onConfirm: () => void;
}) {
  // Focus-trap anchors (T-0049 Copilot review M3 pattern): Tab off the
  // last button cycles to the first via the sentinels, Shift+Tab off
  // the first cycles to the last.
  const firstFocusableRef = useRef<HTMLButtonElement | null>(null);
  const lastFocusableRef = useRef<HTMLButtonElement | null>(null);

  // Esc-to-close + lock background scroll while the dialog is open.
  useEffect(() => {
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onCancel();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', onKey);
    };
  }, [onCancel]);

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={`ship-${orderId}-title`}
      aria-describedby={`ship-${orderId}-description`}
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
    >
      {/* Backdrop: non-focusable, hidden from the a11y tree. The
          accessible close path is the Cancel button + Esc; click-outside
          is a mouse-only convenience. */}
      <div aria-hidden="true" onClick={onCancel} className="absolute inset-0 bg-black/70" />
      {/* Start sentinel: Shift+Tab off Cancel lands here → Confirm. */}
      <div tabIndex={0} onFocus={() => lastFocusableRef.current?.focus()} />
      <div className="relative z-10 w-full max-w-md rounded-2xl border border-zinc-800 bg-surface-card p-6 shadow-2xl">
        <h2 id={`ship-${orderId}-title`} className="text-lg font-semibold text-white">
          {t('dashboard.maker.orderDetail.shipConfirm.title')}
        </h2>
        <p id={`ship-${orderId}-description`} className="mt-2 text-sm text-zinc-400">
          {t('dashboard.maker.orderDetail.shipConfirm.body')}
        </p>
        <div className="mt-6 flex items-center justify-end gap-3">
          {/* autoFocus lands keyboard focus on the safe action when the
              dialog opens (one Tab away from Confirm). */}
          <Button
            ref={firstFocusableRef}
            type="button"
            variant="outline"
            onClick={onCancel}
            autoFocus
          >
            {t('dashboard.maker.orderDetail.shipConfirm.cancel')}
          </Button>
          <Button ref={lastFocusableRef} type="button" onClick={onConfirm}>
            {t('dashboard.maker.orderDetail.shipConfirm.confirm')}
          </Button>
        </div>
      </div>
      {/* End sentinel: Tab off Confirm lands here → Cancel. */}
      <div tabIndex={0} onFocus={() => firstFocusableRef.current?.focus()} />
    </div>
  );
}

interface FileDownloadButtonProps {
  /** Relative maker-host path emitted by the backend (T-0082/T-0088). */
  readonly path: string;
  /** Suggested filename for the saved file. */
  readonly filename: string;
  readonly label: string;
}

/**
 * Authenticated file download for attachments + invoice (T-0086b §C
 * mechanics mirrored on the maker host): blob via the runtime fetch
 * layer + programmatic anchor — a plain `<a href>` to the API host
 * would not carry the audience cookie discipline.
 */
export function FileDownloadButton({ path, filename, label }: FileDownloadButtonProps) {
  const [downloading, setDownloading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleDownload() {
    if (downloading) return;
    setDownloading(true);
    setError(null);

    const result = await downloadMakerOrderFile(path);
    setDownloading(false);
    if (!result.success) {
      setError(resolveErrorMessage(result.error));
      return;
    }

    triggerBlobDownload(result.value, filename);
  }

  return (
    <div className="flex flex-col items-end gap-1">
      <Button
        type="button"
        variant="outline"
        size="sm"
        loading={downloading}
        onClick={() => void handleDownload()}
      >
        <Icon name="download" size={14} />
        {label}
      </Button>
      {error ? <p className="text-sm text-error">{error}</p> : null}
    </div>
  );
}
