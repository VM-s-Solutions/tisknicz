/**
 * Route-segment skeleton for /dashboard/zakaznik/objednavky (T-0086a) —
 * page-header, filter-toolbar and list-box placeholders mirroring the
 * single-container (header row + divided rows) list layout.
 */
export default function CustomerOrdersLoading() {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-7xl flex-col gap-8 px-4 sm:px-6 lg:px-8">
        <div className="flex flex-col gap-3">
          <div className="h-10 w-72 max-w-full animate-pulse rounded-xl bg-zinc-800" />
          <div className="h-5 w-96 max-w-full animate-pulse rounded-lg bg-zinc-800/60" />
        </div>
        <div className="h-40 animate-pulse rounded-xl border border-zinc-800 bg-surface-card sm:h-32" />
        <div className="overflow-hidden rounded-xl border border-zinc-800 bg-surface-card">
          <div className="border-b border-zinc-800 bg-surface-secondary/60 px-4 py-3 sm:px-5">
            <div className="h-4 w-32 animate-pulse rounded-md bg-zinc-800" />
          </div>
          <div className="divide-y divide-zinc-800">
            <div className="h-24 animate-pulse bg-surface-card" />
            <div className="h-24 animate-pulse bg-surface-card" />
            <div className="h-24 animate-pulse bg-surface-card" />
            <div className="h-24 animate-pulse bg-surface-card" />
          </div>
        </div>
      </div>
    </section>
  );
}
