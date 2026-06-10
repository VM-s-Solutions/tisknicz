/**
 * Route-segment skeleton for /objednavka (T-0084a) — summary card +
 * form blocks, mirroring the page's two-column desktop layout.
 */
export default function CheckoutLoading() {
  return (
    <section className="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-10 sm:px-6 lg:px-8">
      <div className="flex flex-col gap-3">
        <div className="h-9 w-56 animate-pulse rounded-xl bg-zinc-800" />
        <div className="h-5 w-80 max-w-full animate-pulse rounded-xl bg-zinc-800/60" />
      </div>
      <div className="flex flex-col gap-8 lg:grid lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start">
        <div className="lg:order-2">
          <div className="h-64 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
        </div>
        <div className="flex flex-col gap-6 lg:order-1">
          <div className="h-80 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
          <div className="h-48 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
          <div className="h-32 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
        </div>
      </div>
    </section>
  );
}
