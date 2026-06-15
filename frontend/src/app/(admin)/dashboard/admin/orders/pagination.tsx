import Link from 'next/link';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

interface PaginationProps {
  readonly page: number;
  readonly totalPages: number;
  readonly hasNext: boolean;
  readonly hasPrevious: boolean;
  /** Filter params to preserve across pages (state/country/maker/customer/dates). */
  readonly baseParams: Readonly<Record<string, string>>;
}

/**
 * URL-driven prev/next pagination for the admin orders list (T-0118a —
 * local copy of the maker objednavky pagination pointed at the admin
 * orders base path). `<Link>`-based so back/forward work and the page
 * stays a Server Component; disabled state uses a non-link span. Only
 * `page > 1` is emitted (`page=1` dropped, patterns.md B.8); the active
 * filters ride along.
 */
const ROUTE_PATH = '/dashboard/admin/orders';

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
      aria-label={t('dashboard.admin.orders.pagination.page_of', { page, total: totalPages })}
      className="mt-10 flex items-center justify-between gap-4"
    >
      {hasPrevious ? (
        <Link
          href={hrefFor(page - 1)}
          className="inline-flex items-center gap-2 rounded-xl border border-zinc-700 px-4 py-2.5 text-sm font-semibold text-zinc-300 transition-colors hover:border-zinc-600 hover:bg-zinc-800"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.admin.orders.pagination.previous')}
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-xl border border-zinc-800 px-4 py-2.5 text-sm font-semibold text-zinc-600"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.admin.orders.pagination.previous')}
        </span>
      )}

      <p className="text-sm text-zinc-400" aria-live="polite">
        {t('dashboard.admin.orders.pagination.page_of', { page, total: totalPages })}
      </p>

      {hasNext ? (
        <Link
          href={hrefFor(page + 1)}
          className="inline-flex items-center gap-2 rounded-xl border border-zinc-700 px-4 py-2.5 text-sm font-semibold text-zinc-300 transition-colors hover:border-zinc-600 hover:bg-zinc-800"
        >
          {t('dashboard.admin.orders.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-xl border border-zinc-800 px-4 py-2.5 text-sm font-semibold text-zinc-600"
        >
          {t('dashboard.admin.orders.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </span>
      )}
    </nav>
  );
}
