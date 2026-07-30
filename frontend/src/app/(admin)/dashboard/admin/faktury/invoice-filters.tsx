'use client';

import Link from 'next/link';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { DatePicker } from '@/components/ui/date-picker';
import { Dropdown } from '@/components/ui/dropdown';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { t } from '@/lib/i18n';

/**
 * Admin all-invoices filter bar (T-0118a, US-admin-0012 AC-1). A native
 * `<form method="get">` writing the URL `searchParams` on submit — the URL
 * stays the single source of truth. The custom `Dropdown`/`DatePicker`
 * primitives are controlled, so their values bridge into the native GET
 * submit via hidden inputs (same wire shape as the previous native
 * `<select>`/`<input type="date">`). `type` is the InvoiceType ordinal
 * (0 = Customer, 1 = Fee). "Vymazat filtry" links to the bare route
 * (drops every param).
 */

const ROUTE_PATH = '/dashboard/admin/faktury';

const TYPE_OPTIONS = [
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
  const [typeValue, setTypeValue] = useState(type);
  const [dateFromValue, setDateFromValue] = useState(dateFrom);
  const [dateToValue, setDateToValue] = useState(dateTo);

  return (
    <form
      method="get"
      action={ROUTE_PATH}
      className="grid grid-cols-1 items-end gap-4 rounded-2xl border border-zinc-800 bg-surface-card p-6 sm:grid-cols-2 lg:grid-cols-3"
    >
      <div className="flex items-center gap-2.5 sm:col-span-2 lg:col-span-3">
        <span aria-hidden="true" className="icon-tile h-8 w-8">
          <Icon name="filter" size={15} />
        </span>
        <h2 className="text-sm font-semibold uppercase tracking-widest text-brand-400">
          {t('dashboard.admin.common.filterHeading')}
        </h2>
      </div>
      <input type="hidden" name="type" value={typeValue} />
      <input type="hidden" name="dateFrom" value={dateFromValue} />
      <input type="hidden" name="dateTo" value={dateToValue} />
      <Dropdown
        label={t('dashboard.admin.invoices.filter.type')}
        value={typeValue}
        onChange={setTypeValue}
        options={TYPE_OPTIONS}
        placeholder={t('dashboard.admin.invoices.filter.typeAll')}
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
      <DatePicker
        label={t('dashboard.admin.invoices.filter.dateFrom')}
        value={dateFromValue}
        onChange={setDateFromValue}
      />
      <DatePicker
        label={t('dashboard.admin.invoices.filter.dateTo')}
        value={dateToValue}
        onChange={setDateToValue}
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
