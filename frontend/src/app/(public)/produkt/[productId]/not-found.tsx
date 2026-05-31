import Link from 'next/link';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

/**
 * 404 surface for the product detail page (T-0048 AC-4). Renders when
 * the page's <c>notFound()</c> fires — the helper returns an
 * <c>ApiError</c> of type <c>NotFound</c> on either an unknown product
 * id, an inactive product, or a product whose owning maker isn't
 * publicly-listable (the backend treats all three as 404 — no oracle
 * leakage).
 */
export default function ProductNotFound() {
  return (
    <section className="mx-auto flex min-h-[calc(100vh-64px)] max-w-3xl flex-col items-center justify-center px-4 py-10 text-center sm:px-6 lg:px-8">
      <p className="text-7xl font-bold tracking-tight text-zinc-800">404</p>
      <h1 className="mt-4 text-2xl font-bold tracking-tight text-white sm:text-3xl">
        {t('catalog.product_detail.not_found.title')}
      </h1>
      <p className="mt-3 text-base text-zinc-500">{t('catalog.product_detail.not_found.body')}</p>
      <div className="mt-8">
        <Link
          href="/katalog"
          className="inline-flex items-center gap-2 rounded-xl border border-brand-400/50 px-6 py-3 text-sm font-semibold text-brand-400 transition-all duration-200 hover:bg-brand-400/10 hover:border-brand-400"
        >
          <Icon name="arrowLeft" size={16} />
          {t('catalog.maker.back_to_catalog')}
        </Link>
      </div>
    </section>
  );
}
