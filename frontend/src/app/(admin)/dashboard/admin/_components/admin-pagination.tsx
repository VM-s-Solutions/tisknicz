import Link from 'next/link';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

interface AdminPaginationProps {
  readonly page: number;
  readonly totalPages: number;
  readonly hasNext: boolean;
  readonly hasPrevious: boolean;
  /** Route the page links target (no query). */
  readonly routePath: string;
  /** URL-state to preserve across pages (filters, search, pageSize). */
  readonly baseParams?: Readonly<Record<string, string>>;
  /** Tighter top margin for the ops lists that used to ship their own copy. */
  readonly spacing?: 'default' | 'tight' | 'none';
  /**
   * Query param carrying the page number. Defaults to `page`; the
   * order-detail audit trail paginates within a page that already owns
   * `page`, so it passes `auditPage`.
   */
  readonly pageParam?: string;
  /** Override the nav's accessible name (a secondary pager on a page needs its own). */
  readonly ariaLabel?: string;
}

const LINK =
  'inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-4 py-2.5 text-sm font-semibold text-zinc-300 transition-colors hover:border-zinc-600 hover:bg-zinc-800';
const LINK_DISABLED =
  'inline-flex cursor-not-allowed items-center gap-2 rounded-lg border border-zinc-800 px-4 py-2.5 text-sm font-semibold text-zinc-500';

/**
 * The ONE admin pagination (T-0175, audit ADM-M1). The surface had grown
 * five near-identical copies — `orders/`, `faktury/`, `audit/`,
 * `ops-pagination` (makers/outbox/vyplaty) and an inline one on the
 * order-detail audit trail — and they had already drifted: the inline
 * copy shipped no "page X of Y" indicator at all. Every consumer now
 * renders the same markup, semantics and indicator; only `routePath` +
 * `baseParams` differ.
 *
 * `<Link>`-based so back/forward work and the page stays a Server
 * Component; the disabled state is a non-link span for keyboard and
 * screen-reader semantics. `page=1` is dropped from the query
 * (patterns.md B.8).
 */
export function AdminPagination({
  page,
  totalPages,
  hasNext,
  hasPrevious,
  routePath,
  baseParams,
  spacing = 'default',
  pageParam = 'page',
  ariaLabel,
}: AdminPaginationProps) {
  if (totalPages <= 1) {
    return null;
  }

  const hrefFor = (target: number): string => {
    const params = new URLSearchParams(baseParams ?? {});
    if (target > 1) params.set(pageParam, String(target));
    const query = params.toString();
    return query ? `${routePath}?${query}` : routePath;
  };

  const label = t('dashboard.admin.pagination.page_of', { page, total: totalPages });
  const margin = spacing === 'none' ? '' : spacing === 'tight' ? 'mt-8' : 'mt-10';

  return (
    <nav
      aria-label={ariaLabel ?? label}
      className={`${margin} flex flex-wrap items-center justify-between gap-3`}
    >
      {hasPrevious ? (
        <Link href={hrefFor(page - 1)} className={LINK}>
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.admin.pagination.previous')}
        </Link>
      ) : (
        <span aria-disabled="true" className={LINK_DISABLED}>
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.admin.pagination.previous')}
        </span>
      )}

      <p className="text-sm text-zinc-400" aria-live="polite">
        {label}
      </p>

      {hasNext ? (
        <Link href={hrefFor(page + 1)} className={LINK}>
          {t('dashboard.admin.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </Link>
      ) : (
        <span aria-disabled="true" className={LINK_DISABLED}>
          {t('dashboard.admin.pagination.next')}
          <Icon name="arrowRight" size={16} />
        </span>
      )}
    </nav>
  );
}
