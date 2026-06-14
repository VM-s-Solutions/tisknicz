import Link from 'next/link';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

interface PaginationProps {
  readonly page: number;
  readonly totalPages: number;
  readonly hasNext: boolean;
  readonly hasPrevious: boolean;
}

/**
 * URL-driven prev/next pagination for the maker review list (T-0117 —
 * local copy of the vyplaty resolution pointed at the `/recenze` base
 * path). `<Link>`-based so the back button works and the page stays a
 * Server Component; disabled state uses a non-link span. Only `page > 1`
 * is emitted (`page=1` dropped, patterns.md B.8). The list takes no other
 * params.
 */
export function Pagination({ page, totalPages, hasNext, hasPrevious }: PaginationProps) {
  if (totalPages <= 1) {
    return null;
  }

  const hrefFor = (target: number): string =>
    target > 1 ? `/dashboard/maker/recenze?page=${target}` : '/dashboard/maker/recenze';

  return (
    <nav
      aria-label={t('dashboard.maker.reviews.pagination.page_of', { page, total: totalPages })}
      className="mt-10 flex items-center justify-between gap-4"
    >
      {hasPrevious ? (
        <Link
          href={hrefFor(page - 1)}
          className="inline-flex items-center gap-2 rounded-xl border border-zinc-700 px-4 py-2.5 text-sm font-semibold text-zinc-300 transition-colors hover:border-zinc-600 hover:bg-zinc-800"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.maker.reviews.pagination.previous')}
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-xl border border-zinc-800 px-4 py-2.5 text-sm font-semibold text-zinc-600"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.maker.reviews.pagination.previous')}
        </span>
      )}

      <p className="text-sm text-zinc-400" aria-live="polite">
        {t('dashboard.maker.reviews.pagination.page_of', { page, total: totalPages })}
      </p>

      {hasNext ? (
        <Link
          href={hrefFor(page + 1)}
          className="inline-flex items-center gap-2 rounded-xl border border-zinc-700 px-4 py-2.5 text-sm font-semibold text-zinc-300 transition-colors hover:border-zinc-600 hover:bg-zinc-800"
        >
          {t('dashboard.maker.reviews.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-xl border border-zinc-800 px-4 py-2.5 text-sm font-semibold text-zinc-600"
        >
          {t('dashboard.maker.reviews.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </span>
      )}
    </nav>
  );
}
