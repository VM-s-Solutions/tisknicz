import type { Metadata } from 'next';
import { Suspense } from 'react';
import { Card } from '@/components/ui/card';
import { Spinner } from '@/components/ui/spinner';
import { t } from '@/lib/i18n';
import { AdminLoginForm } from './admin-login-form';

/**
 * Dedicated admin login page (T-0118a, US-admin-0001). Lives at
 * `/admin/login` on the admin host, OUTSIDE the dashboard session gate
 * (the gate is in `dashboard/admin/layout.tsx`), so an unauthenticated
 * admin reaches it without a redirect loop. Brand-aligned card layout
 * mirroring the `(auth)` shell — admins are operators, not customers, so
 * this is a separate surface from the customer `/login` (ADR 0013).
 */
export const metadata: Metadata = {
  title: t('dashboard.admin.login.metadata.title'),
};

export default function AdminLoginPage() {
  return (
    <div className="relative min-h-screen text-zinc-100">
      <header className="px-6 py-5">
        <span className="text-lg font-semibold tracking-tight">
          {t('dashboard.admin.shell.brand')}
        </span>
      </header>
      <main className="mx-auto flex max-w-md flex-col gap-6 px-6 pb-16 pt-8">
        <div>
          <h1 className="text-2xl font-semibold">{t('dashboard.admin.login.title')}</h1>
          <p className="mt-2 text-sm text-zinc-400">{t('dashboard.admin.login.subtitle')}</p>
        </div>
        {/* Suspense boundary: AdminLoginForm reads useSearchParams() for the
            post-login redirect target, which bails static prerender (the
            customer login page precedent). */}
        <Suspense fallback={<FormSkeleton />}>
          <AdminLoginForm />
        </Suspense>
      </main>
    </div>
  );
}

function FormSkeleton() {
  return (
    <Card padding="lg" className="flex items-center gap-3 text-sm text-zinc-300">
      <Spinner />
      <span>{t('common.loading')}</span>
    </Card>
  );
}
