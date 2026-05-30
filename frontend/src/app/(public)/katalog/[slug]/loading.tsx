import { Card } from '@/components/ui/card';

/**
 * Suspense skeleton for the maker profile page (T-0047). Mirrors the
 * shipped layout: header card, then a 3-column products grid (which
 * collapses to 1/2/3 columns at the same breakpoints as the live page).
 */
export default function Loading() {
  return (
    <main className="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-10 sm:px-6 lg:px-8">
      <Card padding="lg" className="flex flex-col gap-4">
        <div className="h-8 w-2/3 animate-pulse rounded bg-surface-elevated" />
        <div className="h-4 w-1/3 animate-pulse rounded bg-surface-elevated" />
        <div className="space-y-2">
          <div className="h-3 w-full animate-pulse rounded bg-surface-elevated" />
          <div className="h-3 w-5/6 animate-pulse rounded bg-surface-elevated" />
          <div className="h-3 w-4/6 animate-pulse rounded bg-surface-elevated" />
        </div>
      </Card>

      <section className="flex flex-col gap-4">
        <div className="h-6 w-32 animate-pulse rounded bg-surface-elevated" />
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {[0, 1, 2, 3, 4, 5].map((i) => (
            <Card key={i} padding="none" className="overflow-hidden">
              <div className="aspect-[4/3] w-full animate-pulse bg-surface-elevated" />
              <div className="flex flex-col gap-2 p-4">
                <div className="h-4 w-3/4 animate-pulse rounded bg-surface-elevated" />
                <div className="h-3 w-1/3 animate-pulse rounded bg-surface-elevated" />
              </div>
            </Card>
          ))}
        </div>
      </section>
    </main>
  );
}
