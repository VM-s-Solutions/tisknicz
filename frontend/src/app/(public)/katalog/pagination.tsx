import Link from 'next/link';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

interface PaginationProps {
  readonly page: number;
  readonly totalPages: number;
  readonly hasNext: boolean;
  readonly hasPrevious: boolean;
  /** Other search params to preserve (category / city / minRating). */
  readonly baseParams: Readonly<Record<string, string>>;
}

/**
 * URL-driven prev/next pagination. Renders as <Link> elements so back
 * button works and the page stays a Server Component. Disabled state
 * uses a non-Link span to keep keyboard/screen-reader semantics.
 */
export function Pagination({ page, totalPages, hasNext, hasPrevious, baseParams }: PaginationProps) {
  if (totalPages <= 1) {
    return null;
  }

  const hrefFor = (target: number): string => {
    const sp = new URLSearchParams(baseParams);
    sp.set('page', String(target));
    return `/katalog?${sp.toString()}`;
  };

  return (
    <nav
      aria-label={t('catalog.pagination.page_of', { page, total: totalPages })}
      className="mt-8 flex items-center justify-between gap-4 border-t border-zinc-800 pt-6"
    >
      {hasPrevious ? (
        <Link
          href={hrefFor(page - 1)}
          className="inline-flex items-center gap-2 rounded-full border border-zinc-700 px-4 py-2 text-sm font-medium text-zinc-300 transition-all duration-200 hover:border-zinc-500 hover:text-white"
        >
          <Icon name="arrowLeft" size={16} />
          {t('catalog.pagination.previous')}
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-full border border-zinc-800 px-4 py-2 text-sm font-medium text-zinc-600"
        >
          <Icon name="arrowLeft" size={16} />
          {t('catalog.pagination.previous')}
        </span>
      )}

      <p className="text-sm text-zinc-400" aria-live="polite">
        {t('catalog.pagination.page_of', { page, total: totalPages })}
      </p>

      {hasNext ? (
        <Link
          href={hrefFor(page + 1)}
          className="inline-flex items-center gap-2 rounded-full border border-zinc-700 px-4 py-2 text-sm font-medium text-zinc-300 transition-all duration-200 hover:border-zinc-500 hover:text-white"
        >
          {t('catalog.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-full border border-zinc-800 px-4 py-2 text-sm font-medium text-zinc-600"
        >
          {t('catalog.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </span>
      )}
    </nav>
  );
}
