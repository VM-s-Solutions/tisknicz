/**
 * Route-segment skeleton for /objednavka/[id] — heading plus the
 * two-lane tracking layout (status/thread left, money/documents right);
 * the lanes collapse to one stack below lg, matching the page.
 */
export default function OrderLoading() {
  return (
    <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6 lg:px-8">
      <div className="flex flex-col gap-3">
        <div className="h-10 w-72 max-w-full animate-pulse rounded-xl bg-zinc-800" />
        <div className="h-4 w-52 max-w-full animate-pulse rounded-lg bg-zinc-800/60" />
        <div className="h-4 w-64 max-w-full animate-pulse rounded-lg bg-zinc-800/60" />
      </div>
      <div className="mt-8 flex flex-col gap-6 lg:grid lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start lg:gap-8">
        <div className="flex flex-col gap-6">
          <div className="h-72 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
          <div className="h-56 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
        </div>
        <div className="flex flex-col gap-6">
          <div className="h-64 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
          <div className="h-28 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
        </div>
      </div>
    </section>
  );
}
