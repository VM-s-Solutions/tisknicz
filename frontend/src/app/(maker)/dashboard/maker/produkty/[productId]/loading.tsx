import { t } from '@/lib/i18n';

/**
 * Suspense skeleton for the maker product edit page. Mirrors the
 * shipped layout: header, form card, images card.
 */
export default function MakerProductEditLoading() {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-3xl flex-col gap-6 px-4 sm:px-6 lg:px-8">
        <div className="h-5 w-32 animate-pulse rounded bg-surface-elevated" />
        <header>
          <h1 className="text-shine text-3xl font-bold tracking-tight sm:text-4xl">
            {t('dashboard.maker.products.edit.title')}
          </h1>
          <div className="mt-3 h-5 w-2/3 animate-pulse rounded bg-surface-elevated" />
        </header>
        <div className="h-80 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
        <div className="h-56 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
        <div className="h-72 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
      </div>
    </section>
  );
}
