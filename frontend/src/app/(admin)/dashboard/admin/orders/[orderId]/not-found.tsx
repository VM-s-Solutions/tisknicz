import Link from 'next/link';
import { Card } from '@/components/ui/card';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

/**
 * Czech 404 for /dashboard/admin/orders/[orderId] (T-0118b AC-2): a
 * non-existent or unresolvable order id renders one shape (the detail
 * route reads the cross-tenant list + audit-log; neither carrying the id
 * means `notFound()`).
 */
export default function AdminOrderDetailNotFound() {
  return (
    <section className="mx-auto flex max-w-2xl flex-col gap-6 px-4 py-16 sm:px-6 lg:px-8">
      <Card padding="lg" className="flex flex-col items-center gap-4 text-center">
        <h1 className="text-2xl font-semibold text-zinc-50">
          {t('dashboard.admin.orderActions.notFound.title')}
        </h1>
        <p className="text-sm text-zinc-400">
          {t('dashboard.admin.orderActions.notFound.body')}
        </p>
        <Link
          href="/dashboard/admin/orders"
          className="inline-flex items-center gap-2 rounded-lg border border-brand-500/60 px-5 py-2.5 text-sm font-semibold text-brand-300 transition-colors hover:border-brand-500 hover:bg-tint-brand hover:text-on-tint-brand"
        >
          {t('dashboard.admin.orderActions.backToList')}
          <Icon name="arrowRight" size={16} />
        </Link>
      </Card>
    </section>
  );
}
