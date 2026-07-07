/** Route-segment skeleton for /dashboard/admin/makers (T-0140). */
export default function AdminMakersLookupLoading() {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-3xl flex-col gap-6 px-4 sm:px-6 lg:px-8">
        <div className="h-9 w-80 max-w-full animate-pulse rounded-xl bg-zinc-800" />
        <div className="h-5 w-96 max-w-full animate-pulse rounded-xl bg-zinc-800/60" />
        <div className="h-40 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
      </div>
    </section>
  );
}
