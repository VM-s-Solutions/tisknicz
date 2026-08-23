'use client';

import Link from 'next/link';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { t } from '@/lib/i18n';

/**
 * Route-level error boundary for /dashboard/admin (overview) (T-0175, audit ADM-H4/L7). The
 * segment had none, so an SSR throw fell through to Next's raw English
 * screen instead of the Czech retry surface every sibling route has.
 */
export default function AdminOverviewError({
  reset,
}: {
  readonly error: Error & { readonly digest?: string };
  readonly reset: () => void;
}) {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-3xl flex-col gap-4 px-4 sm:px-6 lg:px-8">
        <Alert variant="error">
          <p className="font-semibold">{t('dashboard.admin.overview.error.title')}</p>
          <p className="mt-1">{t('dashboard.admin.overview.error.body')}</p>
        </Alert>
        <div className="flex items-center gap-3">
          <Button type="button" variant="primary" onClick={() => reset()}>
            {t('dashboard.admin.ops.error.retry')}
          </Button>
          <Link
            href="/dashboard/admin"
            className="text-sm font-medium text-zinc-400 transition-colors hover:text-zinc-50"
          >
            {t('dashboard.admin.overview.title')}
          </Link>
        </div>
      </div>
    </section>
  );
}
