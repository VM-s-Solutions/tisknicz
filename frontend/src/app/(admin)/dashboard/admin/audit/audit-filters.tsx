'use client';

import Link from 'next/link';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { DatePicker } from '@/components/ui/date-picker';
import { Icon } from '@/components/ui/icon';
import { Input } from '@/components/ui/input';
import { t } from '@/lib/i18n';

/**
 * Admin audit-log filter bar (T-0118a, US-admin-0015 AC-1). A native
 * `<form method="get">` writing the URL `searchParams` on submit — the
 * URL stays the single source of truth. The custom `DatePicker`
 * primitives are controlled, so their values bridge into the native GET
 * submit via hidden inputs (same wire shape as the previous
 * `<input type="date">`). "Vymazat filtry" links to the bare route
 * (drops every param).
 */

const ROUTE_PATH = '/dashboard/admin/audit';

interface AuditFiltersProps {
  readonly adminUserId: string;
  readonly actionCode: string;
  readonly targetEntity: string;
  readonly dateFrom: string;
  readonly dateTo: string;
}

export function AuditFilters({
  adminUserId,
  actionCode,
  targetEntity,
  dateFrom,
  dateTo,
}: AuditFiltersProps) {
  const [dateFromValue, setDateFromValue] = useState(dateFrom);
  const [dateToValue, setDateToValue] = useState(dateTo);

  return (
    <form
      method="get"
      action={ROUTE_PATH}
      className="grid grid-cols-1 items-end gap-4 rounded-xl border border-zinc-800 bg-surface-card p-6 sm:grid-cols-2 lg:grid-cols-3"
    >
      <div className="flex items-center gap-2.5 sm:col-span-2 lg:col-span-3">
        <span aria-hidden="true" className="icon-tile h-8 w-8">
          <Icon name="filter" size={15} />
        </span>
        <h2 className="text-xs font-semibold uppercase tracking-widest text-zinc-500">
          {t('dashboard.admin.common.filterHeading')}
        </h2>
      </div>
      <input type="hidden" name="dateFrom" value={dateFromValue} />
      <input type="hidden" name="dateTo" value={dateToValue} />
      <Input
        name="adminUserId"
        label={t('dashboard.admin.audit.filter.adminUser')}
        defaultValue={adminUserId}
      />
      <Input
        name="actionCode"
        label={t('dashboard.admin.audit.filter.action')}
        defaultValue={actionCode}
      />
      <Input
        name="targetEntity"
        label={t('dashboard.admin.audit.filter.target')}
        defaultValue={targetEntity}
      />
      <DatePicker
        label={t('dashboard.admin.audit.filter.dateFrom')}
        value={dateFromValue}
        onChange={setDateFromValue}
      />
      <DatePicker
        label={t('dashboard.admin.audit.filter.dateTo')}
        value={dateToValue}
        onChange={setDateToValue}
      />
      <div className="flex items-center gap-3 sm:col-span-2 lg:col-span-3">
        <Button type="submit" variant="primary">
          {t('dashboard.admin.audit.filter.apply')}
        </Button>
        <Link
          href={ROUTE_PATH}
          className="text-sm font-medium text-zinc-400 transition-colors hover:text-white"
        >
          {t('dashboard.admin.audit.filter.reset')}
        </Link>
      </div>
    </form>
  );
}
