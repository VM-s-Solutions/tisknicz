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
 * URL-driven prev/next pagination for the maker product dashboard
 * (mirrors the catalog convention from T-0046). Renders <c>&lt;Link&gt;</c>
 * elements so back-button works and the page stays a Server Component;
 * disabled state uses a non-link span for keyboard/screen-reader
 * semantics.
 */
export function Pagination({ page, totalPages, hasNext, hasPrevious }: PaginationProps) {
  if (totalPages <= 1) {
    return null;
  }

  const hrefFor = (target: number): string => {
    const sp = new URLSearchParams();
    sp.set('page', String(target));
    return `/dashboard/maker/produkty?${sp.toString()}`;
  };

  return (
    <nav
      aria-label={t('dashboard.maker.products.pagination.page_of', { page, total: totalPages })}
      className="mt-10 flex items-center justify-between gap-4"
    >
      {hasPrevious ? (
        <Link
          href={hrefFor(page - 1)}
          className="inline-flex items-center gap-2 rounded-xl border border-zinc-700 px-4 py-2.5 text-sm font-semibold text-zinc-300 transition-colors hover:border-zinc-600 hover:bg-zinc-800"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.maker.products.pagination.previous')}
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-xl border border-zinc-800 px-4 py-2.5 text-sm font-semibold text-zinc-600"
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
          className="inline-flex items-center gap-2 rounded-xl border border-zinc-700 px-4 py-2.5 text-sm font-semibold text-zinc-300 transition-colors hover:border-zinc-600 hover:bg-zinc-800"
        >
          {t('dashboard.maker.products.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-xl border border-zinc-800 px-4 py-2.5 text-sm font-semibold text-zinc-600"
        >
          {t('dashboard.maker.products.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </span>
      )}
    </nav>
  );
}
