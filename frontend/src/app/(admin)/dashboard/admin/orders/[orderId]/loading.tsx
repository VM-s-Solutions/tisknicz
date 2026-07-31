/**
 * Route-segment skeleton for /dashboard/admin/orders/[orderId] (T-0118b)
 * — back link, header card, action bar and audit-trail placeholders
 * mirroring the detail layout (admin orders-list loading precedent).
 */
export default function AdminOrderDetailLoading() {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-4xl flex-col gap-8 px-4 sm:px-6 lg:px-8">
        <div className="h-5 w-32 animate-pulse rounded-xl bg-zinc-800/60" />
        <div className="flex flex-col gap-4">
          <div className="h-10 w-64 max-w-full animate-pulse rounded-xl bg-zinc-800" />
          <div className="h-32 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
        </div>
        <div className="h-14 w-80 max-w-full animate-pulse rounded-xl bg-zinc-800/60" />
        <div className="h-44 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
        <div className="flex flex-col gap-3">
          <div className="h-20 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
          <div className="h-20 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
        </div>
      </div>
    </section>
  );
}
