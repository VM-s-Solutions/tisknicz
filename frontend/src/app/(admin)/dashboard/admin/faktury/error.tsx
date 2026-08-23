'use client';

import Link from 'next/link';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { t } from '@/lib/i18n';

/**
 * Route-level error boundary for the admin invoices list (T-0118a). Last-
 * resort surface — the expected ApiError path renders inline on the page.
 */
export default function AdminInvoicesError({
  reset,
}: {
  readonly error: Error & { readonly digest?: string };
  readonly reset: () => void;
}) {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-3xl flex-col gap-4 px-4 sm:px-6 lg:px-8">
        <Alert variant="error">
          <p className="font-semibold">{t('dashboard.admin.invoices.error.title')}</p>
          <p className="mt-1">{t('dashboard.admin.invoices.error.body')}</p>
        </Alert>
        <div className="flex items-center gap-3">
          <Button type="button" variant="primary" onClick={() => reset()}>
            {t('dashboard.admin.invoices.error.retry')}
          </Button>
          <Link
            href="/dashboard/admin/faktury"
            className="text-sm font-medium text-zinc-400 transition-colors hover:text-zinc-50"
          >
            {t('dashboard.admin.invoices.title')}
          </Link>
        </div>
      </div>
    </section>
  );
}
