'use client';

import { useRef, useState } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { downloadFeeInvoice } from '@/lib/api-client-helpers/payouts-client';
import { t } from '@/lib/i18n';

/**
 * Fee-invoice PDF download button (T-0116, US-maker-0013) — the only client
 * island in the payout route. Blob fetch through the runtime layer
 * (`apiFetch` `parse: 'blob'`, 120 s timeout) + a programmatic anchor named
 * `faktura-{batchNumber}.pdf` — the same mechanism `downloadShippingLabel`
 * uses (a plain `<a href>` would not carry the audience-cookie discipline).
 * Rendered only when `feeInvoiceId != null` (the parent gates it). Errors
 * surface as an inline i18n alert and the button re-enables.
 */

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

interface FeeInvoiceDownloadProps {
  readonly feeInvoiceId: string;
  readonly batchNumber: string;
}

export function FeeInvoiceDownload({ feeInvoiceId, batchNumber }: FeeInvoiceDownloadProps) {
  const [pending, setPending] = useState(false);
  const [error, setError] = useState(false);
  const inFlightRef = useRef(false);

  async function handleDownload(): Promise<void> {
    if (inFlightRef.current) return;
    inFlightRef.current = true;
    setPending(true);
    setError(false);

    const result = await downloadFeeInvoice(feeInvoiceId);
    if (result.success) {
      triggerBlobDownload(result.value, `faktura-${batchNumber}.pdf`);
    } else {
      setError(true);
    }

    inFlightRef.current = false;
    setPending(false);
  }

  return (
    <div className="flex flex-col gap-3">
      <Button
        type="button"
        variant="secondary"
        loading={pending}
        onClick={handleDownload}
        className="w-fit"
      >
        {!pending ? <Icon name="download" size={16} /> : null}
        {pending
          ? t('dashboard.maker.payoutDetail.download.pending')
          : t('dashboard.maker.payoutDetail.download.label')}
      </Button>
      {error ? (
        <Alert variant="error">
          <p className="text-sm">{t('dashboard.maker.payoutDetail.download.error')}</p>
        </Alert>
      ) : null}
    </div>
  );
}
