import { t } from '@/lib/i18n';

/**
 * Skeleton placeholder for the catalog page. Next.js renders this
 * during the server-render await on <see cref="getPagedMakers"/>.
 */
export default function CatalogLoading() {
  return (
    <section className="bg-surface-primary py-20 lg:py-24">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <header className="max-w-4xl">
          <h1 className="text-4xl font-bold tracking-tight text-white sm:text-5xl">
            {t('catalog.title')}
          </h1>
          <p className="mt-6 max-w-3xl text-lg leading-relaxed text-zinc-400">
            {t('catalog.subtitle')}
          </p>
        </header>

        <div className="mt-14 border-y border-zinc-800 py-5">
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-[repeat(3,minmax(0,1fr))_auto]">
            <div className="h-10 animate-pulse rounded-lg bg-zinc-800" />
            <div className="h-10 animate-pulse rounded-lg bg-zinc-800" />
            <div className="h-10 animate-pulse rounded-lg bg-zinc-800" />
            <div className="h-10 animate-pulse rounded-lg bg-zinc-800 lg:w-52" />
          </div>
        </div>

        <div className="mt-10">
          <div className="mb-5 h-4 w-40 animate-pulse rounded bg-zinc-800" />
          <div className="border-y border-zinc-800">
            {Array.from({ length: 6 }).map((_, idx) => (
              <div key={idx} className="h-28 animate-pulse border-b border-zinc-800 bg-zinc-900/20 last:border-b-0" />
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
