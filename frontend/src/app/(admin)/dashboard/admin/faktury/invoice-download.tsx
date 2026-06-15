'use client';

import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

/**
 * Admin invoice "Stáhnout fakturu" affordance (T-0118a, US-admin-0012
 * AC-2 / Option E).
 *
 * The admin invoice-PDF-download endpoint is NOT on the current admin
 * contract (`admin-api.v1.ts` exposes only the payout `csv(id)` bank-file
 * method). Per the ticket, the button ships disabled-with-tooltip and the
 * gap is logged as a thin backend follow-up — it is NEVER wired to a
 * guessed path, and never to the generated `Promise<void>` file method
 * (which discards the body). When the endpoint lands, this island swaps
 * to the blob mechanism (`apiFetch` `parse:'blob'` + a programmatic
 * anchor named `faktura-{invoiceNumber}.pdf` — the maker
 * `fee-invoice-download.tsx` precedent) and `admin-client.ts` gains the
 * `downloadAdminInvoice` helper.
 *
 * Kept as a client island so the disabled→enabled swap is a one-file
 * change in slice/follow-up and the row markup stays stable.
 */
export function InvoiceDownload({ invoiceNumber }: { readonly invoiceNumber: string }) {
  return (
    <span
      aria-disabled="true"
      title={t('dashboard.admin.invoices.download.unavailable')}
      data-invoice-number={invoiceNumber}
      className="inline-flex cursor-not-allowed items-center gap-2 rounded-xl border border-zinc-800 px-3 py-1.5 text-sm font-semibold text-zinc-600"
    >
      <Icon name="download" size={16} />
      {t('dashboard.admin.invoices.download.label')}
    </span>
  );
}
