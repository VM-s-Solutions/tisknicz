import type { AdminInvoiceListItem } from '@/lib/api-client-helpers/admin-client';
import { t } from '@/lib/i18n';
import type { MessageKey } from '@/lib/i18n';
import { formatCzk } from '@/lib/money/formatter';
import { formatDate } from '@/lib/utils/dates';
import { InvoiceDownload } from './invoice-download';

/**
 * Presentational admin invoice rows (T-0118a, US-admin-0012 AC-1).
 * Server-safe markup; the only client island is the per-row download
 * button (currently disabled — the admin invoice-PDF endpoint is a logged
 * backend follow-up, see invoice-download.tsx). Two layouts: stacked
 * cards below `md`, a grid "table" at `md+`. Invoices have no detail page
 * in any slice, so the row is not a `<Link>`.
 *
 * `type` is the InvoiceType ordinal (0 = Customer zákaznická, 1 = Fee
 * provize) — a presentation-only label map, not business logic.
 */

const GRID_COLUMNS =
  'md:grid md:grid-cols-[9rem_7rem_4rem_minmax(0,1fr)_7rem_7rem_9rem] md:items-center md:gap-4';

function invoiceTypeLabelKey(type: number): MessageKey {
  switch (type) {
    case 0:
      return 'dashboard.admin.invoices.type.customer';
    case 1:
      return 'dashboard.admin.invoices.type.fee';
    default:
      return 'dashboard.admin.invoices.type.unknown';
  }
}

export function InvoiceRows({ items }: { readonly items: readonly AdminInvoiceListItem[] }) {
  return (
    <div className="flex flex-col gap-3 md:gap-0">
      <div
        className={`hidden border-b border-zinc-800 px-4 pb-3 text-xs font-semibold uppercase tracking-widest text-zinc-500 ${GRID_COLUMNS}`}
      >
        <span>{t('dashboard.admin.invoices.table.number')}</span>
        <span>{t('dashboard.admin.invoices.table.type')}</span>
        <span>{t('dashboard.admin.invoices.table.country')}</span>
        <span>{t('dashboard.admin.invoices.table.recipient')}</span>
        <span className="md:text-right">{t('dashboard.admin.invoices.table.total')}</span>
        <span className="md:text-right">{t('dashboard.admin.invoices.table.created')}</span>
        <span className="md:text-right">{t('dashboard.admin.invoices.table.actions')}</span>
      </div>
      {items.map((item) => (
        <InvoiceRow key={item.invoiceId} item={item} />
      ))}
    </div>
  );
}

function InvoiceRow({ item }: { readonly item: AdminInvoiceListItem }) {
  return (
    <div
      className={`flex flex-col gap-3 rounded-2xl border border-zinc-800 bg-surface-card p-4 md:rounded-none md:border-x-0 md:border-t-0 md:border-b md:bg-transparent ${GRID_COLUMNS}`}
    >
      <div className="flex items-center justify-between gap-3 md:contents">
        <span className="text-sm font-semibold text-zinc-100">{item.invoiceNumber}</span>
        <span className="hidden text-sm text-zinc-300 md:block">
          {t(invoiceTypeLabelKey(item.type))}
        </span>
        <span className="hidden text-sm text-zinc-400 md:block">{item.countryCode}</span>
        <span className="hidden truncate text-sm text-zinc-300 md:block">{item.recipientName}</span>
        <span className="hidden text-sm font-semibold text-zinc-100 md:block md:text-right">
          {formatCzk(item.totalMinor, item.currency)}
        </span>
        <span className="hidden text-sm text-zinc-400 md:block md:text-right">
          {formatDate(item.createdAt)}
        </span>
        <span className="hidden md:flex md:justify-end">
          <InvoiceDownload invoiceNumber={item.invoiceNumber} />
        </span>
      </div>

      {/* Mobile-only detail block — hidden at md+ where the grid columns render. */}
      <div className="flex flex-col gap-2 md:hidden">
        <p className="truncate text-sm text-zinc-300">{item.recipientName}</p>
        <div className="flex items-center justify-between gap-3 text-sm text-zinc-400">
          <span>
            {t(invoiceTypeLabelKey(item.type))} · {item.countryCode} · {formatDate(item.createdAt)}
          </span>
          <span className="font-semibold text-zinc-100">
            {formatCzk(item.totalMinor, item.currency)}
          </span>
        </div>
        <div className="mt-1">
          <InvoiceDownload invoiceNumber={item.invoiceNumber} />
        </div>
      </div>
    </div>
  );
}
