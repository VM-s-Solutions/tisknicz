import Link from 'next/link';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

/**
 * Czech 404 for /dashboard/maker/vyplaty/[batchId] (T-0116 AC-8): unknown
 * ids AND foreign batches render identically — the backend returns one
 * `payoutBatch.notFound` shape for both (no IDOR oracle, T-0112), so this
 * page deliberately cannot distinguish them either.
 */
export default function MakerPayoutNotFound() {
  return (
    <section className="mx-auto flex max-w-2xl flex-col gap-6 px-4 py-16 sm:px-6 lg:px-8">
      <Card variant="elevated" padding="lg" className="flex flex-col items-center gap-4 text-center">
        <h1 className="text-2xl font-semibold text-zinc-50">
          {t('dashboard.maker.payoutDetail.notFound.title')}
        </h1>
        <p className="text-sm text-zinc-400">{t('dashboard.maker.payoutDetail.notFound.body')}</p>
        <Link
          href="/dashboard/maker/vyplaty"
          className="inline-flex items-center gap-2 rounded-lg border border-brand-line px-5 py-2.5 text-sm font-semibold text-brand-ink transition-colors duration-150 hover:border-brand-500 hover:bg-tint-brand hover:text-on-tint-brand"
        >
          {t('dashboard.maker.payoutDetail.backToList')}
          <Icon name="arrowRight" size={16} />
        </Link>
      </Card>
    </section>
  );
}
