'use client';

import { useRef, useState } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { downloadPayoutCsv } from '@/lib/api-client-helpers/admin-ops-client';
import { t } from '@/lib/i18n';

/**
 * Operator payout-CSV download (T-0118c §3, A.4). The cross-maker bank file
 * — admin/operator-only, INVERTING the T-0116 maker absence. Blob fetch
 * through the runtime layer (`apiFetch` `parse: 'blob'`, 120 s timeout) + a
 * programmatic anchor named `vyplaty-{batchNumber}.csv` — NEVER the
 * generated `csv()` method (NSwag `Promise<void>` discards the body). A
 * plain `<a href>` would not carry the audience-cookie + RFC7807 discipline
 * the runtime fetch layer manages. Errors surface inline + the button
 * re-enables.
 *
 * The download is keyed on the batch id; `batchNumber` (when known) names
 * the file. With no list read, the operator may not know the number — it
 * falls back to the id for the filename.
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

interface PayoutCsvDownloadProps {
  readonly batchId: string;
  /** Batch number for the filename; falls back to the id when unknown (no list read). */
  readonly batchNumber: string;
}

export function PayoutCsvDownload({ batchId, batchNumber }: PayoutCsvDownloadProps) {
  const [pending, setPending] = useState(false);
  const [error, setError] = useState(false);
  const inFlightRef = useRef(false);

  async function handleDownload(): Promise<void> {
    if (inFlightRef.current) return;
    inFlightRef.current = true;
    setPending(true);
    setError(false);

    const result = await downloadPayoutCsv(batchId);
    if (result.success) {
      const namePart = batchNumber.trim() !== '' ? batchNumber.trim() : batchId;
      triggerBlobDownload(result.value, `vyplaty-${namePart}.csv`);
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
        disabled={pending || batchId.trim() === ''}
        onClick={() => void handleDownload()}
        className="w-fit"
      >
        {!pending ? <Icon name="download" size={16} /> : null}
        {pending
          ? t('dashboard.admin.ops.payouts.csv.pending')
          : t('dashboard.admin.ops.payouts.csv.button')}
      </Button>
      {error ? (
        <Alert variant="error">
          <p className="text-sm">{t('dashboard.admin.ops.payouts.csv.error')}</p>
        </Alert>
      ) : null}
    </div>
  );
}
