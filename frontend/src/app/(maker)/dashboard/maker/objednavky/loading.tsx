/**
 * Route-segment skeleton for /dashboard/maker/objednavky (T-0087a) —
 * heading, segmented pill tabs, filter toolbar and lifted-card row
 * placeholders mirroring the list layout.
 */
export default function MakerOrdersLoading() {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-7xl flex-col gap-6 px-4 sm:px-6 lg:px-8">
        <div className="flex flex-col gap-3">
          <div className="h-10 w-72 max-w-full animate-pulse rounded-xl bg-zinc-800" />
          <div className="h-5 w-96 max-w-full animate-pulse rounded-lg bg-zinc-800/60" />
        </div>
        <div className="h-11 w-72 max-w-full animate-pulse rounded-full border border-zinc-800 bg-surface-card" />
        <div className="h-28 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
        <div className="flex flex-col gap-3">
          <div className="h-32 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card sm:h-24" />
          <div className="h-32 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card sm:h-24" />
          <div className="h-32 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card sm:h-24" />
          <div className="h-32 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card sm:h-24" />
        </div>
      </div>
    </section>
  );
}
