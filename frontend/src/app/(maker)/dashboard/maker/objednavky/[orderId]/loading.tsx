/**
 * Route-segment skeleton for /dashboard/maker/objednavky/[orderId]
 * (T-0087b) — back link, heading, then the two-lane detail layout:
 * action bar + timeline + thread left, payout + shipping + contact
 * right. Stacks to one column below lg: like the page itself.
 */
export default function MakerOrderDetailLoading() {
  return (
    <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6 lg:px-8">
      <div className="h-5 w-40 animate-pulse rounded-lg bg-zinc-800/60" />
      <div className="mt-6 h-9 w-72 max-w-full animate-pulse rounded-xl bg-zinc-800" />
      <div className="mt-8 flex flex-col gap-6 lg:grid lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start lg:gap-8">
        <div className="flex min-w-0 flex-col gap-6">
          <div className="h-24 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
          <div className="h-56 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
          <div className="h-40 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
        </div>
        <div className="flex min-w-0 flex-col gap-6">
          <div className="h-48 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
          <div className="h-28 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
          <div className="h-28 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
        </div>
      </div>
    </section>
  );
}
