import Link from 'next/link';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Select } from '@/components/ui/select';
import { t } from '@/lib/i18n';

/**
 * Admin all-invoices filter bar (T-0118a, US-admin-0012 AC-1). A
 * server-rendered native `<form method="get">` writing the URL
 * `searchParams` on submit — no client state (URL is the single source of
 * truth). `type` is the InvoiceType ordinal (0 = Customer, 1 = Fee).
 * "Vymazat filtry" links to the bare route (drops every param).
 */

const ROUTE_PATH = '/dashboard/admin/faktury';

const TYPE_OPTIONS = [
  { value: '', label: t('dashboard.admin.invoices.filter.typeAll') },
  { value: '0', label: t('dashboard.admin.invoices.type.customer') },
  { value: '1', label: t('dashboard.admin.invoices.type.fee') },
];

interface InvoiceFiltersProps {
  readonly type: string;
  readonly country: string;
  readonly recipient: string;
  readonly dateFrom: string;
  readonly dateTo: string;
}

export function InvoiceFilters({ type, country, recipient, dateFrom, dateTo }: InvoiceFiltersProps) {
  return (
    <form
      method="get"
      action={ROUTE_PATH}
      className="grid grid-cols-1 items-end gap-4 rounded-2xl border border-zinc-800 bg-surface-card p-6 sm:grid-cols-2 lg:grid-cols-3"
    >
      <Select
        name="type"
        label={t('dashboard.admin.invoices.filter.type')}
        defaultValue={type}
        options={TYPE_OPTIONS}
      />
      <Input
        name="country"
        label={t('dashboard.admin.invoices.filter.country')}
        defaultValue={country}
        maxLength={2}
        autoCapitalize="characters"
      />
      <Input
        name="recipient"
        label={t('dashboard.admin.invoices.filter.recipient')}
        defaultValue={recipient}
      />
      <Input
        name="dateFrom"
        type="date"
        label={t('dashboard.admin.invoices.filter.dateFrom')}
        defaultValue={dateFrom}
      />
      <Input
        name="dateTo"
        type="date"
        label={t('dashboard.admin.invoices.filter.dateTo')}
        defaultValue={dateTo}
      />
      <div className="flex items-center gap-3 sm:col-span-2 lg:col-span-3">
        <Button type="submit" variant="primary">
          {t('dashboard.admin.invoices.filter.apply')}
        </Button>
        <Link
          href={ROUTE_PATH}
          className="text-sm font-medium text-zinc-400 transition-colors hover:text-white"
        >
          {t('dashboard.admin.invoices.filter.reset')}
        </Link>
      </div>
    </form>
  );
}
