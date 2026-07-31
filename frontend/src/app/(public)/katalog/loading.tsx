import { PageHeader } from '@/components/shared/page-header';
import { Card } from '@/components/ui/card';
import { t } from '@/lib/i18n';

/**
 * Skeleton placeholder for the catalog page. Next.js renders this
 * during the server-render await on <see cref="getPagedMakers"/>.
 * Mirrors the shipped layout: page header, left filter sidebar, then
 * a 1/2-column grid of maker card shells.
 */
export default function CatalogLoading() {
  return (
    <section className="py-14 lg:py-18">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <PageHeader title={t('catalog.title')} subtitle={t('catalog.subtitle')} />

        <div className="mt-10 flex flex-col gap-6 lg:grid lg:grid-cols-[17rem_minmax(0,1fr)] lg:items-start lg:gap-8">
          <aside>
            <Card variant="elevated" padding="sm" className="sm:p-5">
              <div className="flex items-center gap-2.5">
                <div className="h-8 w-8 animate-pulse rounded-xl bg-surface-elevated" />
                <div className="h-4 w-24 animate-pulse rounded bg-surface-elevated" />
              </div>
              <div className="mt-5 hidden flex-col gap-3 lg:flex">
                {Array.from({ length: 5 }).map((_, idx) => (
                  <div key={idx} className="h-9 animate-pulse rounded-lg bg-surface-elevated" />
                ))}
                <div className="h-11 animate-pulse rounded-lg bg-surface-elevated" />
              </div>
            </Card>
          </aside>

          <div className="min-w-0">
            <div className="mb-5 h-4 w-40 animate-pulse rounded bg-zinc-800" />
            <div className="grid grid-cols-1 gap-4 xl:grid-cols-2 xl:gap-5">
              {Array.from({ length: 6 }).map((_, idx) => (
                <div key={idx} className="panel rounded-xl border border-zinc-800 p-5 sm:p-6">
                  <div className="flex items-start gap-4">
                    <div className="h-12 w-12 shrink-0 animate-pulse rounded-xl bg-surface-elevated" />
                    <div className="min-w-0 flex-1 space-y-2">
                      <div className="h-5 w-1/2 animate-pulse rounded bg-surface-elevated" />
                      <div className="h-4 w-1/4 animate-pulse rounded bg-surface-elevated" />
                    </div>
                  </div>
                  <div className="mt-4 space-y-2">
                    <div className="h-3 w-full animate-pulse rounded bg-surface-elevated" />
                    <div className="h-3 w-2/3 animate-pulse rounded bg-surface-elevated" />
                  </div>
                  <div className="mt-4 border-t border-zinc-800/80 pt-4">
                    <div className="h-4 w-1/3 animate-pulse rounded bg-surface-elevated" />
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
