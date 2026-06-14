/**
 * Route-segment skeleton for /dashboard/maker/recenze (T-0117) — heading
 * + card placeholders mirroring the review list layout (vyplaty precedent;
 * roomier blocks because review cards carry a comment + a reply form).
 */
export default function MakerReviewsLoading() {
  return (
    <section className="bg-surface-primary py-12 lg:py-16">
      <div className="mx-auto flex max-w-4xl flex-col gap-6 px-4 sm:px-6 lg:px-8">
        <div className="h-9 w-64 max-w-full animate-pulse rounded-xl bg-zinc-800" />
        <div className="h-5 w-96 max-w-full animate-pulse rounded-xl bg-zinc-800/60" />
        <div className="flex flex-col gap-4">
          <div className="h-40 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
          <div className="h-40 animate-pulse rounded-2xl border border-zinc-800 bg-surface-card" />
        </div>
      </div>
    </section>
  );
}
