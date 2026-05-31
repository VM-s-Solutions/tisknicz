/**
 * Suspense skeleton for the product detail page (T-0048). Mirrors the
 * shipped layout: square image shimmer on the left, title + price +
 * info lines on the right (stacked at narrow widths, side-by-side at
 * lg).
 */
export default function Loading() {
  return (
    <section className="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-10 sm:px-6 lg:px-8">
      <div className="flex flex-col gap-8 lg:grid lg:grid-cols-[minmax(0,1fr)_24rem] lg:gap-8">
        <div className="flex flex-col gap-4">
          <div className="aspect-square w-full animate-pulse rounded-2xl bg-surface-elevated" />
          <div className="flex gap-2">
            {[0, 1, 2, 3].map((i) => (
              <div key={i} className="h-20 w-20 animate-pulse rounded-xl bg-surface-elevated" />
            ))}
          </div>
        </div>

        <div className="flex flex-col gap-5">
          <div className="h-9 w-3/4 animate-pulse rounded bg-surface-elevated" />
          <div className="h-7 w-1/3 animate-pulse rounded bg-surface-elevated" />
          <div className="h-4 w-1/2 animate-pulse rounded bg-surface-elevated" />
          <div className="h-4 w-1/4 animate-pulse rounded bg-surface-elevated" />
          <div className="mt-2 h-12 w-full animate-pulse rounded-xl bg-surface-elevated sm:w-40" />
        </div>
      </div>

      <div className="flex flex-col gap-3">
        <div className="h-6 w-24 animate-pulse rounded bg-surface-elevated" />
        <div className="space-y-2">
          <div className="h-3 w-full animate-pulse rounded bg-surface-elevated" />
          <div className="h-3 w-5/6 animate-pulse rounded bg-surface-elevated" />
          <div className="h-3 w-4/6 animate-pulse rounded bg-surface-elevated" />
        </div>
      </div>
    </section>
  );
}
