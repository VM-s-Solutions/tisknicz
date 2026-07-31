import { Suspense } from 'react';
import { Card } from '@/components/ui/card';
import { Spinner } from '@/components/ui/spinner';
import { VerifyClient } from './verify-client';
import { t } from '@/lib/i18n';

export const metadata = {
  title: 'Potvrzení e-mailu — Makables',
};

export default function VerifyPage() {
  return (
    <div className="mx-auto w-full max-w-md">
      <h1 className="text-2xl font-semibold tracking-tight text-white sm:text-3xl">{t('auth.verify.title')}</h1>
      <div className="mt-6">
        <Suspense fallback={<ConfirmingSkeleton />}>
          <VerifyClient />
        </Suspense>
      </div>
    </div>
  );
}

function ConfirmingSkeleton() {
  return (
    <Card padding="lg" variant="elevated" className="flex items-center gap-3 text-sm text-zinc-300">
      <Spinner />
      <span>{t('auth.verify.confirming')}</span>
    </Card>
  );
}
