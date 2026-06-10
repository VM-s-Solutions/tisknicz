/**
 * Route-segment skeleton for /objednavka/[id] (T-0084b) — heading,
 * breakdown card, pay CTA and attachment-manager blocks.
 */
export default function OrderLoading() {
  return (
    <section className="mx-auto flex max-w-3xl flex-col gap-6 px-4 py-10 sm:px-6 lg:px-8">
      <div className="h-9 w-72 max-w-full animate-pulse rounded-xl bg-zinc-800" />
      <div className="h-14 animate-pulse rounded-xl bg-zinc-800/60" />
      <div className="h-56 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
      <div className="h-24 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
      <div className="h-12 animate-pulse rounded-xl bg-zinc-800" />
      <div className="h-40 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
    </section>
  );
}
