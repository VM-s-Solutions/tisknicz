/**
 * Route-segment skeleton for /dashboard/zakaznik/objednavky (T-0086a) —
 * page-header, filter-toolbar and order-card placeholders mirroring the
 * lifted-card list layout.
 */
export default function CustomerOrdersLoading() {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-7xl flex-col gap-8 px-4 sm:px-6 lg:px-8">
        <div className="flex flex-col gap-3">
          <div className="h-10 w-72 max-w-full animate-pulse rounded-xl bg-zinc-800" />
          <div className="h-5 w-96 max-w-full animate-pulse rounded-lg bg-zinc-800/60" />
        </div>
        <div className="h-40 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card sm:h-32" />
        <div className="flex flex-col gap-3">
          <div className="h-28 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
          <div className="h-28 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
          <div className="h-28 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
          <div className="h-28 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
        </div>
      </div>
    </section>
  );
}
