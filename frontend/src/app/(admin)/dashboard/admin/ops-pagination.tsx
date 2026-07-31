import Link from 'next/link';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

interface OpsPaginationProps {
  readonly page: number;
  readonly totalPages: number;
  readonly hasNext: boolean;
  readonly hasPrevious: boolean;
  /** The route path the pagination links target (no query). */
  readonly routePath: string;
  /**
   * Extra URL-state params to preserve across page links (T-0119b — the
   * makers list carries a `search` term). Omitted by the param-less
   * outbox/payout lists.
   */
  readonly extraParams?: Readonly<Record<string, string>>;
}

/**
 * URL-driven prev/next pagination for the admin-ops browsable lists (T-0127
 * §8, US-admin-0007/0014) — the local copy of the T-0087a maker-list
 * resolution per the ticket's "match T-0087a, do not invent a third
 * variant" guidance. `<Link>`-based so the back button works and the page
 * stays a Server Component; disabled state uses a non-link span for
 * keyboard/screen-reader semantics. Only `page > 1` is emitted (`page=1` is
 * dropped, patterns.md B.8). The outbox + payout lists carry no other
 * URL-state params, so the link query is just the page.
 */
export function OpsPagination({
  page,
  totalPages,
  hasNext,
  hasPrevious,
  routePath,
  extraParams,
}: OpsPaginationProps) {
  if (totalPages <= 1) {
    return null;
  }

  const hrefFor = (target: number): string => {
    const params = new URLSearchParams(extraParams ?? {});
    if (target > 1) params.set('page', String(target));
    const query = params.toString();
    return query ? `${routePath}?${query}` : routePath;
  };

  return (
    <nav
      aria-label={t('dashboard.admin.ops.pagination.pageOf', { page, total: totalPages })}
      className="mt-8 flex flex-wrap items-center justify-between gap-3"
    >
      {hasPrevious ? (
        <Link
          href={hrefFor(page - 1)}
          className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-4 py-2.5 text-sm font-semibold text-zinc-300 transition-colors hover:border-zinc-600 hover:bg-zinc-800"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.admin.ops.pagination.previous')}
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-lg border border-zinc-800 px-4 py-2.5 text-sm font-semibold text-zinc-500"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.admin.ops.pagination.previous')}
        </span>
      )}

      <p className="text-sm text-zinc-400" aria-live="polite">
        {t('dashboard.admin.ops.pagination.pageOf', { page, total: totalPages })}
      </p>

      {hasNext ? (
        <Link
          href={hrefFor(page + 1)}
          className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-4 py-2.5 text-sm font-semibold text-zinc-300 transition-colors hover:border-zinc-600 hover:bg-zinc-800"
        >
          {t('dashboard.admin.ops.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-lg border border-zinc-800 px-4 py-2.5 text-sm font-semibold text-zinc-500"
        >
          {t('dashboard.admin.ops.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </span>
      )}
    </nav>
  );
}
