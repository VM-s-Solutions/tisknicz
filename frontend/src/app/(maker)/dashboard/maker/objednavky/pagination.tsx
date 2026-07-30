import Link from 'next/link';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

interface PaginationProps {
  readonly page: number;
  readonly totalPages: number;
  readonly hasNext: boolean;
  readonly hasPrevious: boolean;
  /** Other search params to preserve (tab / dateFrom / dateTo / sort / pageSize). */
  readonly baseParams: Readonly<Record<string, string>>;
}

/**
 * URL-driven prev/next pagination for the maker order list (T-0087a —
 * local copy of the T-0086a customer-list resolution per the ticket's
 * §C "match T-0086a, do not invent a third variant"). `<Link>`-based so
 * the back button works and the page stays a Server Component; disabled
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
      ? `/dashboard/maker/objednavky?${query}`
      : '/dashboard/maker/objednavky';
  };

  return (
    <nav
      aria-label={t('dashboard.maker.orders.pagination.page_of', { page, total: totalPages })}
      className="mt-10 flex items-center justify-between gap-4"
    >
      {hasPrevious ? (
        <Link
          href={hrefFor(page - 1)}
          className="inline-flex items-center gap-2 rounded-full border border-zinc-700 px-5 py-2.5 text-sm font-semibold text-zinc-300 transition-colors hover:border-brand-500/40 hover:bg-brand-400/5 hover:text-brand-300"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.maker.orders.pagination.previous')}
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-full border border-zinc-800 px-5 py-2.5 text-sm font-semibold text-zinc-600"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.maker.orders.pagination.previous')}
        </span>
      )}

      <p className="text-sm text-zinc-400" aria-live="polite">
        {t('dashboard.maker.orders.pagination.page_of', { page, total: totalPages })}
      </p>

      {hasNext ? (
        <Link
          href={hrefFor(page + 1)}
          className="inline-flex items-center gap-2 rounded-full border border-zinc-700 px-5 py-2.5 text-sm font-semibold text-zinc-300 transition-colors hover:border-brand-500/40 hover:bg-brand-400/5 hover:text-brand-300"
        >
          {t('dashboard.maker.orders.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </Link>
      ) : (
        <span
          aria-disabled="true"
          className="inline-flex cursor-not-allowed items-center gap-2 rounded-full border border-zinc-800 px-5 py-2.5 text-sm font-semibold text-zinc-600"
        >
          {t('dashboard.maker.orders.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </span>
      )}
    </nav>
  );
}
