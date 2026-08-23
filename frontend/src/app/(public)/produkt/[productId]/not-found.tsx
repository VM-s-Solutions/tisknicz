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
      <p aria-hidden="true" className="text-6xl font-bold tracking-tight text-zinc-700 sm:text-7xl">
        404
      </p>
      <h1 className="mt-4 text-2xl font-bold tracking-tight text-zinc-50 sm:text-3xl">
        {t('catalog.product_detail.not_found.title')}
      </h1>
      <p className="mt-3 text-base text-zinc-400">{t('catalog.product_detail.not_found.body')}</p>
      <div className="mt-8">
        <Link
          href="/katalog"
          className="inline-flex items-center gap-2 rounded-lg border border-brand-500/60 px-5 py-2.5 text-sm font-semibold text-brand-300 transition-colors duration-150 hover:border-brand-400 hover:bg-tint-brand hover:text-brand-200"
        >
          <Icon name="arrowLeft" size={16} />
          {t('catalog.maker.back_to_catalog')}
        </Link>
      </div>
    </section>
  );
}
