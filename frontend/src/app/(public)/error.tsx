'use client';

import Link from 'next/link';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

/**
 * Route-level error boundary for the whole public surface (T-0171, audit
 * PUB-M1). Every other route group ships per-route boundaries, but the
 * ONE surface anonymous visitors actually see had none: an unhandled
 * render throw — a category-cache parse, a date format, anything outside
 * the handled `Result` failures — fell through to Next's default English
 * screen with no navigation, no Czech copy and no way back.
 *
 * This sits inside the `(public)` layout, so the navbar and footer stay
 * rendered: a visitor who hits it can still browse instead of being
 * dead-ended on a bare page.
 */
export default function PublicError({
  reset,
}: {
  readonly error: Error & { readonly digest?: string };
  readonly reset: () => void;
}) {
  return (
    <section className="py-14 lg:py-18">
      <div className="mx-auto flex max-w-2xl flex-col gap-5 px-4 sm:px-6 lg:px-8">
        <Alert variant="error">
          <p className="font-semibold">{t('publicError.title')}</p>
          <p className="mt-1 text-sm">{t('publicError.body')}</p>
        </Alert>
        <div className="flex flex-wrap items-center gap-3">
          <Button type="button" variant="primary" onClick={() => reset()}>
            <Icon name="refresh" size={16} />
            {t('common.retry')}
          </Button>
          <Link
            href="/katalog"
            className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-5 py-2.5 text-sm font-semibold text-zinc-200 transition-colors duration-150 hover:border-brand-500/60 hover:text-brand-300"
          >
            {t('notFound.browse_catalog')}
          </Link>
          <Link
            href="/"
            className="text-sm font-medium text-zinc-400 transition-colors hover:text-zinc-200"
          >
            {t('notFound.back_home')}
          </Link>
        </div>
      </div>
    </section>
  );
}
