import { t } from '@/lib/i18n';

/**
 * Suspense skeleton for the maker product index. Mirrors the shipped
 * grid: header line, count line, then a 2/3/4-column card grid that
 * collapses on narrow widths.
 */
export default function MakerProductsLoading() {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <header className="mb-8 flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <h1 className="text-shine text-3xl font-bold tracking-tight sm:text-4xl">
              {t('dashboard.maker.products.title')}
            </h1>
            <p className="mt-3 max-w-2xl text-base text-zinc-400">
              {t('dashboard.maker.products.subtitle')}
            </p>
          </div>
          <div className="h-10 w-40 animate-pulse rounded-lg bg-surface-elevated" />
        </header>

        <div className="mb-6 h-4 w-32 animate-pulse rounded bg-surface-elevated" />
        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, idx) => (
            <div
              key={idx}
              className="h-96 animate-pulse rounded-xl border border-zinc-800 bg-surface-card"
            />
          ))}
        </div>
      </div>
    </section>
  );
}
