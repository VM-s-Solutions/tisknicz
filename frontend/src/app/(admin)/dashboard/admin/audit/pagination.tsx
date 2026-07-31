import Link from 'next/link';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

interface PaginationProps {
  readonly page: number;
  readonly totalPages: number;
  readonly hasNext: boolean;
  readonly hasPrevious: boolean;
  /** Filter params to preserve across pages (adminUser/action/target/dates). */
  readonly baseParams: Readonly<Record<string, string>>;
}

/**
 * URL-driven prev/next pagination for the admin audit-log list (T-0118a —
 * local copy pointed at the `/audit` base path). `<Link>`-based; only
 * `page > 1` is emitted (B.8); active filters ride along.
 */
const ROUTE_PATH = '/dashboard/admin/audit';

export function Pagination({ page, totalPages, hasNext, hasPrevious, baseParams }: PaginationProps) {
  if (totalPages <= 1) {
    return null;
  }

  const hrefFor = (target: number): string => {
    const sp = new URLSearchParams(baseParams);
    if (target > 1) sp.set('page', String(target));
    const query = sp.toString();
    return query ? `${ROUTE_PATH}?${query}` : ROUTE_PATH;
  };

  return (
    <nav
      aria-label={t('dashboard.admin.audit.pagination.page_of', { page, total: totalPages })}
      className="mt-10 flex flex-wrap items-center justify-between gap-3"
    >
      {hasPrevious ? (
        <Link
          href={hrefFor(page - 1)}
          className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-4 py-2.5 text-sm font-semibold text-zinc-300 transition-colors hover:border-zinc-600 hover:bg-zinc-800"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.admin.audit.pagination.previous')}
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-lg border border-zinc-800 px-4 py-2.5 text-sm font-semibold text-zinc-500"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.admin.audit.pagination.previous')}
        </span>
      )}

      <p className="text-sm text-zinc-400" aria-live="polite">
        {t('dashboard.admin.audit.pagination.page_of', { page, total: totalPages })}
      </p>

      {hasNext ? (
        <Link
          href={hrefFor(page + 1)}
          className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-4 py-2.5 text-sm font-semibold text-zinc-300 transition-colors hover:border-zinc-600 hover:bg-zinc-800"
        >
          {t('dashboard.admin.audit.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-lg border border-zinc-800 px-4 py-2.5 text-sm font-semibold text-zinc-500"
        >
          {t('dashboard.admin.audit.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </span>
      )}
    </nav>
  );
}
