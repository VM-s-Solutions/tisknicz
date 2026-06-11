'use client';

import { useRouter } from 'next/navigation';
import { useRef, useState } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { downloadOrderFile, markOrderDelivered } from '@/lib/api-client-helpers/orders-client';
import { t } from '@/lib/i18n';
import { resolveErrorMessage } from '@/lib/runtime/errors';

/**
 * Client islands for the tracking detail (T-0086b): the confirm-delivery
 * button (rendered by the server page ONLY when `state === Shipped` —
 * T-0082 §C conditional-rendering stance) and the authenticated file
 * download buttons (blob + programmatic anchor; the streaming endpoints
 * need the audience cookie, so a plain `<a href>` to the API host is not
 * an option — T-0086b §C download mechanics).
 */

export function MarkDeliveredButton({ orderId }: { readonly orderId: string }) {
  const router = useRouter();
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inFlightRef = useRef(false);

  async function handleClick() {
    if (inFlightRef.current) return;
    inFlightRef.current = true;
    setSubmitting(true);
    setError(null);

    const result = await markOrderDelivered(orderId);
    if (result.success) {
      // Server re-render flips the badge to Doručeno and drops this
      // button (Q5 lock — re-sync via router.refresh(), no client store).
      router.refresh();
      return;
    }

    setError(resolveErrorMessage(result.error));
    inFlightRef.current = false;
    setSubmitting(false);
  }

  return (
    <div className="flex flex-col gap-3">
      {error ? <Alert variant="error">{error}</Alert> : null}
      <Button type="button" size="lg" loading={submitting} onClick={() => void handleClick()}>
        {submitting
          ? t('customer.orderDetail.markDelivered.inFlight')
          : t('customer.orders.markDeliveredButton')}
      </Button>
    </div>
  );
}

interface FileDownloadButtonProps {
  /** Relative customer-host path emitted by the backend (T-0082/T-0088). */
  readonly path: string;
  /** Suggested filename for the saved file. */
  readonly filename: string;
  readonly label: string;
}

export function FileDownloadButton({ path, filename, label }: FileDownloadButtonProps) {
  const [downloading, setDownloading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleDownload() {
    if (downloading) return;
    setDownloading(true);
    setError(null);

    const result = await downloadOrderFile(path);
    setDownloading(false);
    if (!result.success) {
      setError(resolveErrorMessage(result.error));
      return;
    }

    const objectUrl = URL.createObjectURL(result.value);
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
