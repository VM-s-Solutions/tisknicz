'use client';

import Link from 'next/link';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { t } from '@/lib/i18n';

/**
 * Last-resort error boundary for /dashboard/admin/vyplaty (T-0118c).
 * Expected ApiErrors surface inline via the Result flow; this catches an
 * unhandled render/network throw.
 */
export default function AdminPayoutsError({
  reset,
}: {
  readonly error: Error & { readonly digest?: string };
  readonly reset: () => void;
}) {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-3xl flex-col gap-4 px-4 sm:px-6 lg:px-8">
        <Alert variant="error">
          <p className="font-semibold">{t('dashboard.admin.ops.error.title')}</p>
          <p className="mt-1">{t('dashboard.admin.ops.error.body')}</p>
        </Alert>
        <div className="flex items-center gap-3">
          <Button type="button" variant="primary" onClick={() => reset()}>
            {t('dashboard.admin.ops.error.retry')}
          </Button>
          <Link
            href="/dashboard/admin"
            className="text-sm font-medium text-zinc-400 transition-colors hover:text-white"
          >
            {t('dashboard.admin.nav.overview')}
          </Link>
        </div>
      </div>
    </section>
  );
}
