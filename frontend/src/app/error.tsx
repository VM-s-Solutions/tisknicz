'use client';

import Link from 'next/link';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

/**
 * Last-resort error boundary for anything that throws OUTSIDE a route
 * group's own boundary (T-0171, audit PUB-M1) — the landing page and the
 * root-level routes had nothing above them, so a throw there rendered
 * Next's default English screen.
 *
 * Deliberately self-contained: this renders in the ROOT layout, without
 * the public chrome, so it cannot assume the navbar's session helpers
 * resolved (they may be exactly what failed).
 */
export default function RootError({
  reset,
}: {
  readonly error: Error & { readonly digest?: string };
  readonly reset: () => void;
}) {
  return (
    <div className="flex min-h-screen items-center justify-center bg-surface-primary px-4">
      <div className="flex w-full max-w-lg flex-col gap-5">
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
            href="/"
            className="inline-flex items-center gap-2 rounded-lg border border-zinc-700 px-5 py-2.5 text-sm font-semibold text-zinc-200 transition-colors duration-150 hover:border-brand-line hover:text-brand-300"
          >
            {t('notFound.back_home')}
          </Link>
        </div>
      </div>
    </div>
  );
}
