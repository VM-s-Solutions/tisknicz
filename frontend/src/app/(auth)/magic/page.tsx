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
      <h1 className="text-2xl font-semibold tracking-tight text-zinc-50 sm:text-3xl">{t('auth.magic.title')}</h1>
      <div className="mt-6">
        <Suspense fallback={<ConsumingSkeleton />}>
          <MagicClient />
        </Suspense>
      </div>
    </div>
  );
}

function ConsumingSkeleton() {
  return (
    <Card padding="lg" variant="elevated" className="flex items-center gap-3 text-sm text-zinc-300">
      <Spinner />
      <span>{t('auth.magic.consuming')}</span>
    </Card>
  );
}
