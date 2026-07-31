/**
 * Route-segment skeleton for /dashboard/admin/users (T-0118c) — heading +
 * lookup-card placeholder.
 */
export default function AdminUsersLoading() {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-2xl flex-col gap-6 px-4 sm:px-6 lg:px-8">
        <div className="h-9 w-72 max-w-full animate-pulse rounded-xl bg-zinc-800" />
        <div className="h-5 w-96 max-w-full animate-pulse rounded-xl bg-zinc-800/60" />
        <div className="h-72 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
      </div>
    </section>
  );
}
