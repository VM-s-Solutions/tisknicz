import { t } from '@/lib/i18n';

/**
 * Skeleton placeholder for the catalog page. Next.js renders this
 * during the server-render await on <see cref="getPagedMakers"/>.
 */
export default function CatalogLoading() {
  return (
    <section className="bg-surface-primary py-16 lg:py-24">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <header className="mb-10">
          <h1 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t('catalog.title')}
          </h1>
          <p className="mt-3 max-w-2xl text-base text-zinc-400">
            {t('catalog.subtitle')}
          </p>
        </header>

        <div className="grid grid-cols-1 gap-8 lg:grid-cols-[18rem_minmax(0,1fr)]">
          <aside>
            <div className="h-96 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
          </aside>

          <div>
            <div className="mb-6 h-4 w-32 animate-pulse rounded bg-zinc-800" />
            <div className="grid grid-cols-1 gap-5 md:grid-cols-2 lg:grid-cols-3">
              {Array.from({ length: 6 }).map((_, idx) => (
                <div
                  key={idx}
                  className="h-52 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card"
                />
              ))}
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
