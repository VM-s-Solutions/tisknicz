import { Suspense } from 'react';
import { Card } from '@/components/ui/card';
import { Spinner } from '@/components/ui/spinner';
import { ResetClient } from './reset-client';
import { t } from '@/lib/i18n';

export const metadata = {
  title: 'Obnova hesla — Makables',
};

export default function ResetPage() {
  return (
    <div className="mx-auto w-full max-w-md">
      <Suspense fallback={<Skeleton />}>
        <ResetClient />
      </Suspense>
    </div>
  );
}

function Skeleton() {
  return (
    <Card padding="lg" className="flex items-center gap-3 text-sm text-zinc-300">
      <Spinner />
      <span>{t('common.loading')}</span>
    </Card>
  );
}
