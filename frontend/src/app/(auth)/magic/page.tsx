import { Suspense } from 'react';
import { Card } from '@/components/ui/card';
import { Spinner } from '@/components/ui/spinner';
import { MagicClient } from './magic-client';
import { t } from '@/lib/i18n';

export const metadata = {
  title: 'Přihlášení odkazem — Makables',
};

export default function MagicPage() {
  return (
    <div className="mx-auto w-full max-w-md">
      <h1 className="text-2xl font-semibold">{t('auth.magic.title')}</h1>
      <Suspense fallback={<ConsumingSkeleton />}>
        <MagicClient />
      </Suspense>
    </div>
  );
}

function ConsumingSkeleton() {
  return (
    <Card padding="lg" className="flex items-center gap-3 text-sm text-zinc-300">
      <Spinner />
      <span>{t('auth.magic.consuming')}</span>
    </Card>
  );
}
