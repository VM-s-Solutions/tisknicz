/**
 * Route-segment skeleton for /dashboard/zakaznik/objednavky (T-0086a) —
 * heading, filter bar and row placeholders mirroring the list layout.
 */
export default function CustomerOrdersLoading() {
  return (
    <section className="bg-surface-primary py-12 lg:py-16">
      <div className="mx-auto flex max-w-7xl flex-col gap-8 px-4 sm:px-6 lg:px-8">
        <div className="h-9 w-72 max-w-full animate-pulse rounded-xl bg-zinc-800" />
        <div className="h-28 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
        <div className="flex flex-col gap-3">
          <div className="h-20 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
          <div className="h-20 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
          <div className="h-20 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
          <div className="h-20 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
        </div>
      </div>
    </section>
  );
}
