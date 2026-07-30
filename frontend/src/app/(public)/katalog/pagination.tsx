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

const PILL_ACTIVE =
  'inline-flex items-center gap-2 rounded-full border border-zinc-700 bg-surface-card px-4 py-2 text-sm font-medium text-zinc-300 transition-all duration-200 hover:border-brand-400/60 hover:text-brand-200 hover:shadow-lg hover:shadow-brand-500/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/40';

const PILL_DISABLED =
  'inline-flex cursor-not-allowed items-center gap-2 rounded-full border border-zinc-800 px-4 py-2 text-sm font-medium text-zinc-600';

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
    <nav aria-label={t('catalog.pagination.page_of', { page, total: totalPages })} className="mt-10">
      <div aria-hidden="true" className="divider-glow" />
      <div className="mt-6 flex items-center justify-between gap-4">
        {hasPrevious ? (
          <Link href={hrefFor(page - 1)} className={PILL_ACTIVE}>
            <Icon name="arrowLeft" size={16} />
            {t('catalog.pagination.previous')}
          </Link>
        ) : (
          <span aria-disabled="true" className={PILL_DISABLED}>
            <Icon name="arrowLeft" size={16} />
            {t('catalog.pagination.previous')}
          </span>
        )}

        <p className="text-sm text-zinc-400" aria-live="polite">
          {t('catalog.pagination.page_of', { page, total: totalPages })}
        </p>

        {hasNext ? (
          <Link href={hrefFor(page + 1)} className={PILL_ACTIVE}>
            {t('catalog.pagination.next')}
            <Icon name="arrowRight" size={16} />
          </Link>
        ) : (
          <span aria-disabled="true" className={PILL_DISABLED}>
            {t('catalog.pagination.next')}
            <Icon name="arrowRight" size={16} />
          </span>
        )}
      </div>
    </nav>
  );
}
