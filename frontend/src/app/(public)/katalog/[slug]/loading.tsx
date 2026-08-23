import { Card } from '@/components/ui/card';

/**
 * Suspense skeleton for the maker profile page (T-0047). Mirrors the
 * shipped layout: accent hero panel with avatar tile, then a products
 * section panel with a 1/2/3-column grid of elevated card shells.
 */
export default function Loading() {
  return (
    <section className="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-10 sm:px-6 lg:px-8">
      <Card variant="elevated" padding="lg" className="flex flex-col gap-5">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:gap-5">
          <div className="h-16 w-16 shrink-0 animate-pulse rounded-xl bg-surface-elevated" />
          <div className="flex min-w-0 flex-1 flex-col gap-3">
            <div className="h-9 w-2/3 animate-pulse rounded bg-surface-elevated" />
            <div className="h-4 w-1/3 animate-pulse rounded bg-surface-elevated" />
          </div>
        </div>
        <div className="space-y-2">
          <div className="h-3 w-full animate-pulse rounded bg-surface-elevated" />
          <div className="h-3 w-5/6 animate-pulse rounded bg-surface-elevated" />
          <div className="h-3 w-4/6 animate-pulse rounded bg-surface-elevated" />
        </div>
      </Card>

      <Card padding="md" className="flex flex-col gap-5">
        <div className="flex items-center gap-3">
          <div className="h-9 w-9 animate-pulse rounded-xl bg-surface-elevated" />
          <div className="h-6 w-32 animate-pulse rounded bg-surface-elevated" />
        </div>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {[0, 1, 2, 3, 4, 5].map((i) => (
            <Card key={i} variant="elevated" padding="none" className="overflow-hidden">
              <div className="aspect-[4/3] w-full animate-pulse bg-surface-elevated" />
              <div className="flex flex-col gap-2 p-4">
                <div className="h-4 w-3/4 animate-pulse rounded bg-surface-elevated" />
                <div className="h-3 w-1/3 animate-pulse rounded bg-surface-elevated" />
              </div>
            </Card>
          ))}
        </div>
      </Card>
    </section>
  );
}
