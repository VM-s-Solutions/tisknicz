import { Badge } from '@/components/ui/badge';
import { Tooltip } from '@/components/ui/tooltip';
import type { AdminAuditLogItem } from '@/lib/api-client-helpers/admin-client';
import { t } from '@/lib/i18n';
import { formatDateTime } from '@/lib/utils/dates';

/**
 * Presentational admin audit-log rows (T-0118a, US-admin-0015 AC-1).
 * Server-safe: pure formatting. The row is NOT a `<Link>` in slice a —
 * the diff-detail route (`/dashboard/admin/audit/[id]`) does not exist
 * until slice c (the side-by-side before/after JSON diff), so a live link
 * would 404 (review L4). Slice c re-wraps it. Rendered as one
 * GitHub-style "box": a single bordered container with a header row
 * (title + count) and `divide-y` rows — not floating cards. Two row
 * layouts: stacked cards below `md`, a grid "table" at `md+`. `createdAt`
 * shows Czech short date + time via `formatDateTime`.
 */

const GRID_COLUMNS =
  'md:grid md:grid-cols-[9.5rem_minmax(0,1fr)_minmax(0,1fr)_8rem_minmax(0,1fr)] md:items-center md:gap-4';

export function AuditRows({
  items,
  totalCount,
}: {
  readonly items: readonly AdminAuditLogItem[];
  readonly totalCount: number;
}) {
  return (
    <div className="rounded-xl border border-zinc-800 bg-surface-card">
      <div className="flex items-center justify-between gap-3 rounded-t-xl border-b border-zinc-800 bg-surface-secondary/60 px-4 py-3">
        <div className="flex items-center gap-2.5">
          <h2 className="text-sm font-semibold text-zinc-100">
            {t('dashboard.admin.audit.title')}
          </h2>
          <Badge dot={false} aria-label={t('dashboard.admin.audit.count', { count: totalCount })}>
            {totalCount}
          </Badge>
        </div>
      </div>
      <div
        className={`hidden border-b border-zinc-800 px-4 py-3 text-xs font-semibold uppercase tracking-widest text-zinc-500 ${GRID_COLUMNS}`}
      >
        <span>{t('dashboard.admin.audit.table.created')}</span>
        <span>{t('dashboard.admin.audit.table.action')}</span>
        <span>{t('dashboard.admin.audit.table.target')}</span>
        <span>{t('dashboard.admin.audit.table.adminUser')}</span>
        <span>{t('dashboard.admin.audit.table.notes')}</span>
      </div>
      <div className="divide-y divide-zinc-800">
        {items.map((item) => (
          <AuditRow key={item.id} item={item} />
        ))}
      </div>
    </div>
  );
}

function AuditRow({ item }: { readonly item: AdminAuditLogItem }) {
  const notes = item.notes && item.notes.trim() !== ''
    ? item.notes
    : t('dashboard.admin.audit.notesPlaceholder');

  return (
    <div className={`flex flex-col gap-3 p-4 ${GRID_COLUMNS}`}>
      <div className="flex items-center justify-between gap-3 md:contents">
        <span className="text-sm text-zinc-400">{formatDateTime(item.createdAt)}</span>
        <span className="text-sm font-semibold text-zinc-100 md:truncate">{item.actionCode}</span>
        {/* Tooltips carry the FULL ids (data, not new copy) — the grid
            columns truncate GUIDs beyond usefulness at md+. */}
        <Tooltip content={`${item.targetEntity} · ${item.targetId}`} className="min-w-0 max-md:hidden">
          <span className="min-w-0 truncate text-sm text-zinc-300">
            {item.targetEntity}
            <span className="text-zinc-500"> · {item.targetId}</span>
          </span>
        </Tooltip>
        <Tooltip content={item.adminUserId} className="min-w-0 max-md:hidden">
          <span className="min-w-0 truncate text-sm text-zinc-400">{item.adminUserId}</span>
        </Tooltip>
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
