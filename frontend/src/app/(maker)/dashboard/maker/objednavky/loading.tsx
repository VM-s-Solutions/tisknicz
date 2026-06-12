/**
 * Route-segment skeleton for /dashboard/maker/objednavky (T-0087a) —
 * heading, tab strip, filter bar and row placeholders mirroring the
 * list layout (T-0086a customer-list precedent).
 */
export default function MakerOrdersLoading() {
  return (
    <section className="bg-surface-primary py-12 lg:py-16">
      <div className="mx-auto flex max-w-7xl flex-col gap-6 px-4 sm:px-6 lg:px-8">
        <div className="h-9 w-72 max-w-full animate-pulse rounded-xl bg-zinc-800" />
        <div className="h-10 w-80 max-w-full animate-pulse rounded-xl bg-zinc-800/60" />
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
