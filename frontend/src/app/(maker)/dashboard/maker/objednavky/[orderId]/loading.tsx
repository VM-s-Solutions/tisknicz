/**
 * Route-segment skeleton for /dashboard/maker/objednavky/[orderId]
 * (T-0087b) — back link, heading, action bar, payout card, timeline and
 * thread blocks mirroring the detail layout.
 */
export default function MakerOrderDetailLoading() {
  return (
    <section className="mx-auto flex max-w-3xl flex-col gap-6 px-4 py-10 sm:px-6 lg:px-8">
      <div className="h-5 w-40 animate-pulse rounded-lg bg-zinc-800/60" />
      <div className="h-9 w-72 max-w-full animate-pulse rounded-xl bg-zinc-800" />
      <div className="h-12 w-56 max-w-full animate-pulse rounded-xl bg-zinc-800" />
      <div className="h-48 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
      <div className="h-56 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
      <div className="h-24 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
      <div className="h-40 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
    </section>
  );
}
