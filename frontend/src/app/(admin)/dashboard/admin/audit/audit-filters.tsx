import Link from 'next/link';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { t } from '@/lib/i18n';

/**
 * Admin audit-log filter bar (T-0118a, US-admin-0015 AC-1). A
 * server-rendered native `<form method="get">` writing the URL
 * `searchParams` on submit — no client state. "Vymazat filtry" links to
 * the bare route (drops every param).
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
  return (
    <form
      method="get"
      action={ROUTE_PATH}
      className="grid grid-cols-1 items-end gap-4 rounded-2xl border border-zinc-800 bg-surface-card p-6 sm:grid-cols-2 lg:grid-cols-3"
    >
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
      <Input
        name="dateFrom"
        type="date"
        label={t('dashboard.admin.audit.filter.dateFrom')}
        defaultValue={dateFrom}
      />
      <Input
        name="dateTo"
        type="date"
        label={t('dashboard.admin.audit.filter.dateTo')}
        defaultValue={dateTo}
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
