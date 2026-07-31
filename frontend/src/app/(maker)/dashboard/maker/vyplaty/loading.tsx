/**
 * Route-segment skeleton for /dashboard/maker/vyplaty (T-0116) — heading
 * and the boxed list (header strip + hairline-divided rows) mirroring
 * the list layout (objednavky precedent).
 */
export default function MakerPayoutsLoading() {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-7xl flex-col gap-6 px-4 sm:px-6 lg:px-8">
        <div className="h-10 w-72 max-w-full animate-pulse rounded-xl bg-zinc-800" />
        <div className="h-5 w-96 max-w-full animate-pulse rounded-lg bg-zinc-800/60" />
        <div className="overflow-hidden rounded-xl border border-zinc-800 bg-surface-card">
          <div className="h-11 border-b border-zinc-800 bg-surface-secondary/60" />
          <div className="divide-y divide-zinc-800">
            <div className="h-32 animate-pulse bg-zinc-800/30 sm:h-24" />
            <div className="h-32 animate-pulse bg-zinc-800/30 sm:h-24" />
            <div className="h-32 animate-pulse bg-zinc-800/30 sm:h-24" />
          </div>
        </div>
      </div>
    </section>
  );
}
