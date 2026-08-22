/**
 * Route-segment skeleton for /dashboard/admin/kategorie (T-0175, audit ADM-H4). The segment
 * shipped no `loading.tsx`, so a slow SSR read showed the previous page
 * frozen with zero feedback (the overview alone fires six probes).
 */
export default function AdminCategoriesLoading() {
  return (
    <section className="py-12 lg:py-16">
      <div className="mx-auto flex max-w-4xl flex-col gap-6 px-4 sm:px-6 lg:px-8">
        <div className="h-9 w-72 max-w-full animate-pulse rounded-xl bg-zinc-800" />
        <div className="h-5 w-96 max-w-full animate-pulse rounded-xl bg-zinc-800/60" />
        <div className="h-20 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
        <div className="h-72 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
      </div>
    </section>
  );
}
