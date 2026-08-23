import Link from 'next/link';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

interface PaginationProps {
  readonly page: number;
  readonly totalPages: number;
  readonly hasNext: boolean;
  readonly hasPrevious: boolean;
  /** Other search params to preserve (state / dateFrom / dateTo / sort). */
  readonly baseParams: Readonly<Record<string, string>>;
}

/**
 * URL-driven prev/next pagination for the customer order list (T-0086a
 * — copied katalog precedent per the ticket's §C). `<Link>`-based so the
 * back button works and the page stays a Server Component; disabled
 * state uses a non-link span for keyboard/screen-reader semantics. Only
 * non-default params are emitted (`page=1` is dropped, patterns.md B.8).
 */
export function Pagination({ page, totalPages, hasNext, hasPrevious, baseParams }: PaginationProps) {
  if (totalPages <= 1) {
    return null;
  }

  const hrefFor = (target: number): string => {
    const sp = new URLSearchParams(baseParams);
    if (target > 1) {
      sp.set('page', String(target));
    }
    const query = sp.toString();
    return query
      ? `/dashboard/zakaznik/objednavky?${query}`
      : '/dashboard/zakaznik/objednavky';
  };

  return (
    <nav
      aria-label={t('customer.orders.pagination.page_of', { page, total: totalPages })}
      className="mt-10 flex flex-wrap items-center justify-center gap-4 sm:justify-between"
    >
      {hasPrevious ? (
        <Link
          href={hrefFor(page - 1)}
          className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-5 py-2.5 text-sm font-medium text-zinc-200 transition-colors duration-150 hover:border-brand-line hover:text-brand-300"
        >
          <Icon name="arrowLeft" size={16} />
          {t('customer.orders.pagination.previous')}
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-lg border border-zinc-800 px-5 py-2.5 text-sm font-medium text-zinc-500"
        >
          <Icon name="arrowLeft" size={16} />
          {t('customer.orders.pagination.previous')}
        </span>
      )}

      <p
        className="rounded-md border border-zinc-800 px-4 py-1.5 text-sm text-zinc-400"
        aria-live="polite"
      >
        {t('customer.orders.pagination.page_of', { page, total: totalPages })}
      </p>

      {hasNext ? (
        <Link
          href={hrefFor(page + 1)}
          className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-5 py-2.5 text-sm font-medium text-zinc-200 transition-colors duration-150 hover:border-brand-line hover:text-brand-300"
        >
          {t('customer.orders.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-lg border border-zinc-800 px-5 py-2.5 text-sm font-medium text-zinc-500"
        >
          {t('customer.orders.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </span>
      )}
    </nav>
  );
}
