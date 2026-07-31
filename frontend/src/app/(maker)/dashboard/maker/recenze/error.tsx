'use client';

import Link from 'next/link';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { t } from '@/lib/i18n';

/**
 * Route-level error boundary for the maker review dashboard. Renders when
 * a Server Component throws (network blip, unhandled render exception).
 * The expected-error path (backend reachable but returning an ApiError)
 * surfaces inline on the page itself (Result flow), so this boundary is
 * the last-resort surface (vyplaty precedent).
 */
export default function MakerReviewsError({
  reset,
}: {
  readonly error: Error & { readonly digest?: string };
  readonly reset: () => void;
}) {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-3xl flex-col gap-4 px-4 sm:px-6 lg:px-8">
        <Alert variant="error">
          <p className="font-semibold">{t('dashboard.maker.reviews.error.title')}</p>
          <p className="mt-1">{t('dashboard.maker.reviews.error.body')}</p>
        </Alert>
        <div className="flex items-center gap-3">
          <Button type="button" variant="primary" onClick={() => reset()}>
            {t('dashboard.maker.reviews.error.retry')}
          </Button>
          <Link
            href="/dashboard/maker/recenze"
            className="text-sm text-zinc-400 transition-colors hover:text-zinc-200"
          >
            {t('dashboard.maker.reviews.title')}
          </Link>
        </div>
      </div>
    </section>
  );
}
