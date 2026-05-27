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
    <>
      <h1 className="text-2xl font-semibold">{t('auth.verify.title')}</h1>
      <Suspense fallback={<ConfirmingSkeleton />}>
        <VerifyClient />
      </Suspense>
    </>
  );
}

function ConfirmingSkeleton() {
  return (
    <Card padding="lg" className="flex items-center gap-3 text-sm text-zinc-300">
      <Spinner />
      <span>{t('auth.verify.confirming')}</span>
    </Card>
  );
}
