'use client';

import Link from 'next/link';
import { Alert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { t } from '@/lib/i18n';

/**
 * Route-level error boundary for the admin orders list (T-0118a). Renders
 * when the Server Component throws (network blip, unhandled render
 * exception). The expected-error path (backend reachable but returning an
 * ApiError) surfaces inline on the page itself (Result flow), so this is
 * the last-resort surface (maker objednavky precedent).
 */
export default function AdminOrdersError({
  reset,
}: {
  readonly error: Error & { readonly digest?: string };
  readonly reset: () => void;
}) {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-3xl flex-col gap-4 px-4 sm:px-6 lg:px-8">
        <Alert variant="error">
          <p className="font-semibold">{t('dashboard.admin.orders.error.title')}</p>
          <p className="mt-1">{t('dashboard.admin.orders.error.body')}</p>
        </Alert>
        <div className="flex items-center gap-3">
          <Button type="button" variant="primary" onClick={() => reset()}>
            {t('dashboard.admin.orders.error.retry')}
          </Button>
          <Link
            href="/dashboard/admin/orders"
            className="text-sm font-medium text-zinc-400 transition-colors hover:text-zinc-50"
          >
            {t('dashboard.admin.orders.title')}
          </Link>
        </div>
      </div>
    </section>
  );
}
