'use client';

import Link from 'next/link';
import type { MouseEvent } from 'react';
import { useNavigationTransition } from '@/components/shared/navigation-transition';
import { Icon } from '@/components/ui/icon';
import { Spinner } from '@/components/ui/spinner';
import { t } from '@/lib/i18n';

interface PaginationProps {
  readonly page: number;
  readonly totalPages: number;
  readonly hasNext: boolean;
  readonly hasPrevious: boolean;
  /**
   * Serialized search params to preserve (category / city / minRating),
   * `page` excluded. A query STRING rather than a record because
   * `category` is repeatable for the multi-select filter and a record
   * cannot hold two values for one key.
   */
  readonly baseQuery: string;
}

const PAGE_LINK =
  'inline-flex items-center gap-2 rounded-lg border border-zinc-700 bg-surface-card px-4 py-2 text-sm font-medium text-zinc-200 transition-colors duration-150 hover:border-brand-line hover:text-brand-300 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400/40';

const PAGE_LINK_DISABLED =
  'inline-flex cursor-not-allowed items-center gap-2 rounded-lg border border-zinc-800 px-4 py-2 text-sm font-medium text-zinc-500';

/**
 * True when the browser will handle the click itself (new tab, new
 * window, download) instead of letting the router navigate in place. In
 * those cases the current page must stay where it is.
 */
function opensElsewhere(event: MouseEvent<HTMLAnchorElement>): boolean {
  return event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey;
}

/**
 * URL-driven prev/next pagination. Renders as <Link> elements so the
 * back button and crawlers both see real hrefs; the disabled state uses
 * a non-Link span to keep keyboard/screen-reader semantics.
 *
 * T-0170 (audit PUB-H2): plain clicks route through
 * {@link useNavigationTransition} — the surrounding provider dims the
 * results while the server render is in flight and the scroll-to-top
 * fires only once the NEW page's data arrived. The old behavior
 * scrolled on click, teleporting the reader to the top of the OLD page
 * which then silently swapped. Modified clicks (new tab etc.) keep the
 * real href.
 */
export function Pagination({ page, totalPages, hasNext, hasPrevious, baseQuery }: PaginationProps) {
  const { pending, navigate } = useNavigationTransition();

  if (totalPages <= 1) {
    return null;
  }

  const hrefFor = (target: number): string => {
    const sp = new URLSearchParams(baseQuery);
    sp.set('page', String(target));
    return `/katalog?${sp.toString()}`;
  };

  const handleNavigate = (event: MouseEvent<HTMLAnchorElement>, href: string): void => {
    if (opensElsewhere(event)) return;
    event.preventDefault();
    navigate(href, { scrollTop: true });
  };

  return (
    <nav aria-label={t('catalog.pagination.page_of', { page, total: totalPages })} className="mt-10">
      <div aria-hidden="true" className="divider-glow" />
      <div className="mt-6 flex flex-wrap items-center justify-between gap-x-4 gap-y-3">
        {hasPrevious && !pending ? (
          <Link
            href={hrefFor(page - 1)}
            scroll={false}
            onClick={(event) => handleNavigate(event, hrefFor(page - 1))}
            className={PAGE_LINK}
          >
            <Icon name="arrowLeft" size={16} />
            {t('catalog.pagination.previous')}
          </Link>
        ) : (
          <span aria-disabled="true" className={PAGE_LINK_DISABLED}>
            <Icon name="arrowLeft" size={16} />
            {t('catalog.pagination.previous')}
          </span>
        )}

        <p className="flex items-center gap-2 text-sm text-zinc-400" aria-live="polite">
          {pending ? <Spinner size="sm" /> : null}
          {pending
            ? t('catalog.results.loading')
            : t('catalog.pagination.page_of', { page, total: totalPages })}
        </p>

        {hasNext && !pending ? (
          <Link
            href={hrefFor(page + 1)}
            scroll={false}
            onClick={(event) => handleNavigate(event, hrefFor(page + 1))}
            className={PAGE_LINK}
          >
            {t('catalog.pagination.next')}
            <Icon name="arrowRight" size={16} />
          </Link>
        ) : (
          <span aria-disabled="true" className={PAGE_LINK_DISABLED}>
            {t('catalog.pagination.next')}
            <Icon name="arrowRight" size={16} />
          </span>
        )}
      </div>
    </nav>
  );
}
