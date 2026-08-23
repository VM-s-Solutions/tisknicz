import Link from 'next/link';
import { Icon } from '@/components/ui/icon';
import { t } from '@/lib/i18n';

/**
 * 404 surface for the maker product edit page (T-0049 AC-7). Renders
 * when <c>notFound()</c> fires — the helper returns <c>ApiError</c>
 * of type <c>NotFound</c> on either an unknown product id or a product
 * owned by a different maker. The backend collapses both to 404 (IDOR
 * shield) so this page must not lean on either reason in its copy.
 */
export default function MakerProductNotFound() {
  return (
    <section className="mx-auto flex min-h-[calc(100vh-64px)] max-w-3xl flex-col items-center justify-center px-4 py-10 text-center sm:px-6 lg:px-8">
      <p aria-hidden="true" className="text-7xl font-bold tracking-tight text-zinc-700">
        404
      </p>
      <h1 className="mt-4 text-2xl font-bold tracking-tight text-zinc-50 sm:text-3xl">
        {t('dashboard.maker.products.edit.not_found.title')}
      </h1>
      <p className="mt-3 text-base text-zinc-500">
        {t('dashboard.maker.products.edit.not_found.body')}
      </p>
      <div className="mt-8">
        <Link
          href="/dashboard/maker/produkty"
          className="inline-flex items-center gap-2 rounded-lg border border-brand-line px-5 py-2.5 text-sm font-semibold text-brand-ink transition-colors duration-150 hover:border-brand-500 hover:bg-brand-fill-soft"
        >
          <Icon name="arrowLeft" size={16} />
          {t('dashboard.maker.products.edit.back')}
        </Link>
      </div>
    </section>
  );
}
