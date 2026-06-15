import type { AdminAuditLogItem } from '@/lib/api-client-helpers/admin-client';
import { t } from '@/lib/i18n';
import { formatDateTime } from '@/lib/utils/dates';

/**
 * Presentational admin audit-log rows (T-0118a, US-admin-0015 AC-1).
 * Server-safe: pure formatting. The row is NOT a `<Link>` in slice a —
 * the diff-detail route (`/dashboard/admin/audit/[id]`) does not exist
 * until slice c (the side-by-side before/after JSON diff), so a live link
 * would 404 (review L4). Slice c re-wraps it. Two layouts: stacked cards below `md`,
 * a grid "table" at `md+`. `createdAt` shows Czech short date + time via
 * `formatDateTime`.
 */

const GRID_COLUMNS =
  'md:grid md:grid-cols-[9.5rem_minmax(0,1fr)_minmax(0,1fr)_8rem_minmax(0,1fr)] md:items-center md:gap-4';

export function AuditRows({ items }: { readonly items: readonly AdminAuditLogItem[] }) {
  return (
    <div className="flex flex-col gap-3 md:gap-0">
      <div
        className={`hidden border-b border-zinc-800 px-4 pb-3 text-xs font-semibold uppercase tracking-widest text-zinc-500 ${GRID_COLUMNS}`}
      >
        <span>{t('dashboard.admin.audit.table.created')}</span>
        <span>{t('dashboard.admin.audit.table.action')}</span>
        <span>{t('dashboard.admin.audit.table.target')}</span>
        <span>{t('dashboard.admin.audit.table.adminUser')}</span>
        <span>{t('dashboard.admin.audit.table.notes')}</span>
      </div>
      {items.map((item) => (
        <AuditRow key={item.id} item={item} />
      ))}
    </div>
  );
}

function AuditRow({ item }: { readonly item: AdminAuditLogItem }) {
  const notes = item.notes && item.notes.trim() !== ''
    ? item.notes
    : t('dashboard.admin.audit.notesPlaceholder');

  return (
    <div
      className={`flex flex-col gap-3 rounded-2xl border border-zinc-800 bg-surface-card p-4 md:rounded-none md:border-x-0 md:border-t-0 md:border-b md:bg-transparent ${GRID_COLUMNS}`}
    >
      <div className="flex items-center justify-between gap-3 md:contents">
        <span className="text-sm text-zinc-400">{formatDateTime(item.createdAt)}</span>
        <span className="text-sm font-semibold text-zinc-100 md:truncate">{item.actionCode}</span>
        <span className="hidden truncate text-sm text-zinc-300 md:block">
          {item.targetEntity}
          <span className="text-zinc-500"> · {item.targetId}</span>
        </span>
        <span className="hidden truncate text-sm text-zinc-400 md:block">{item.adminUserId}</span>
        <span className="hidden truncate text-sm text-zinc-400 md:block">{notes}</span>
      </div>

      {/* Mobile-only detail block — hidden at md+ where the grid columns render. */}
      <div className="flex flex-col gap-1 md:hidden">
        <p className="truncate text-sm text-zinc-300">
          {item.targetEntity}
          <span className="text-zinc-500"> · {item.targetId}</span>
        </p>
        <p className="truncate text-sm text-zinc-500">
          {t('dashboard.admin.audit.table.adminUser')}: {item.adminUserId}
        </p>
        <p className="truncate text-sm text-zinc-500">{notes}</p>
      </div>
    </div>
  );
}
