import Link from 'next/link';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

/**
 * Czech 404 for /objednavka/[id] (T-0084b AC-9): unknown ids AND
 * foreign orders render identically — the backend returns 404 for both
 * (IDOR-resistant).
 */
export default function OrderNotFound() {
  return (
    <section className="mx-auto flex max-w-2xl flex-col gap-6 px-4 py-16 sm:px-6 lg:px-8">
      <Card padding="lg" className="flex flex-col items-center gap-4 text-center">
        <h1 className="text-2xl font-semibold text-white">{t('order.page.notFound.title')}</h1>
        <p className="text-sm text-zinc-400">{t('order.page.notFound.body')}</p>
        <Link
          href="/katalog"
          className="inline-flex items-center gap-2 rounded-xl bg-brand-400 px-5 py-2.5 text-sm font-semibold text-zinc-950 transition-all duration-200 hover:bg-brand-300"
        >
          {t('order.page.banner.backToCatalog')}
          <Icon name="arrowRight" size={16} />
        </Link>
      </Card>
    </section>
  );
}
