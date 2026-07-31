import Link from 'next/link';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

interface PaginationProps {
  readonly page: number;
  readonly totalPages: number;
  readonly hasNext: boolean;
  readonly hasPrevious: boolean;
  /**
   * Forward-roundtripped from the page's <c>searchParams.pageSize</c>
   * so deep-links and share-links preserve the chosen window size. The
   * page passes the resolved value (post-default, post-clamp) so the
   * link always reflects what's actually rendered, not what the user
   * originally typed. T-0049 Copilot review M2.
   */
  readonly pageSize: number;
  /** The default the page falls back to — only emitted when different. */
  readonly defaultPageSize: number;
}

/**
 * URL-driven prev/next pagination for the maker product dashboard
 * (mirrors the catalog convention from T-0046). Renders <c>&lt;Link&gt;</c>
 * elements so back-button works and the page stays a Server Component;
 * disabled state uses a non-link span for keyboard/screen-reader
 * semantics.
 */
export function Pagination({ page, totalPages, hasNext, hasPrevious, pageSize, defaultPageSize }: PaginationProps) {
  if (totalPages <= 1) {
    return null;
  }

  const hrefFor = (target: number): string => {
    const sp = new URLSearchParams();
    sp.set('page', String(target));
    // Only emit pageSize when it diverges from the default so canonical
    // URLs stay clean (`?page=2` not `?page=2&pageSize=24`).
    if (pageSize !== defaultPageSize) {
      sp.set('pageSize', String(pageSize));
    }
    return `/dashboard/maker/produkty?${sp.toString()}`;
  };

  return (
    <nav
      aria-label={t('dashboard.maker.products.pagination.page_of', { page, total: totalPages })}
      className="mt-10 flex flex-wrap items-center justify-between gap-3 sm:gap-4"
    >
      {hasPrevious ? (
        <Link
          href={hrefFor(page - 1)}
          className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-5 py-2.5 text-sm font-semibold text-zinc-200 transition-colors hover:border-brand-500/60 hover:text-brand-300"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.maker.products.pagination.previous')}
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-lg border border-zinc-800 px-5 py-2.5 text-sm font-semibold text-zinc-500"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.maker.products.pagination.previous')}
        </span>
      )}

      <p className="text-sm text-zinc-400" aria-live="polite">
        {t('dashboard.maker.products.pagination.page_of', { page, total: totalPages })}
      </p>

      {hasNext ? (
        <Link
          href={hrefFor(page + 1)}
          className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-5 py-2.5 text-sm font-semibold text-zinc-200 transition-colors hover:border-brand-500/60 hover:text-brand-300"
        >
          {t('dashboard.maker.products.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-lg border border-zinc-800 px-5 py-2.5 text-sm font-semibold text-zinc-500"
        >
          {t('dashboard.maker.products.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </span>
      )}
    </nav>
  );
}
