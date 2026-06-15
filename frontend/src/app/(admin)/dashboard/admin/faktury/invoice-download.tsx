'use client';

import { useRef, useState } from 'react';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { downloadAdminInvoice } from '@/lib/api-client-helpers/admin-ops-client';
import { t } from '@/lib/i18n';

/**
 * Admin invoice "Stáhnout fakturu" download (T-0118a → re-enabled in
 * T-0118c). The admin invoice-PDF endpoint (T-0126,
 * `GET /admin-invoices/{id}/pdf`) now exists, so the disabled-with-tooltip
 * placeholder is replaced with a real blob download. Same discipline as the
 * payout CSV / maker fee-invoice: `apiFetch` `parse: 'blob'` + a
 * programmatic anchor named `faktura-{invoiceNumber}.pdf` — NEVER the
 * generated `pdf()` method (NSwag types it `Promise<void>` and discards the
 * body). A plain `<a href>` would not carry the audience-cookie + RFC7807
 * discipline. Errors surface inline + the button re-enables.
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

interface InvoiceDownloadProps {
  readonly invoiceId: string;
  readonly invoiceNumber: string;
}

export function InvoiceDownload({ invoiceId, invoiceNumber }: InvoiceDownloadProps) {
  const [pending, setPending] = useState(false);
  const [error, setError] = useState(false);
  const inFlightRef = useRef(false);

  async function handleDownload(): Promise<void> {
    if (inFlightRef.current) return;
    inFlightRef.current = true;
    setPending(true);
    setError(false);

    const result = await downloadAdminInvoice(invoiceId);
    if (result.success) {
      triggerBlobDownload(result.value, `faktura-${invoiceNumber}.pdf`);
    } else {
      setError(true);
    }

    inFlightRef.current = false;
    setPending(false);
  }

  return (
    <div className="flex flex-col items-end gap-2">
      <Button
        type="button"
        variant="secondary"
        size="sm"
        loading={pending}
        onClick={() => void handleDownload()}
      >
        {!pending ? <Icon name="download" size={16} /> : null}
        {pending
          ? t('dashboard.admin.invoices.download.downloading')
          : t('dashboard.admin.invoices.download.label')}
      </Button>
      {error ? (
        <Alert variant="error">
          <p className="text-sm">{t('dashboard.admin.invoices.download.error')}</p>
        </Alert>
      ) : null}
    </div>
  );
}
