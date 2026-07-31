/**
 * Route-segment skeleton for /objednavka/[id]/potvrzeni (T-0085) — a
 * single centered confirmation card.
 */
export default function PaymentConfirmationLoading() {
  return (
    <section className="mx-auto flex max-w-2xl flex-col gap-6 px-4 py-16 sm:px-6 lg:px-8">
      <div className="h-80 animate-pulse rounded-xl border border-zinc-800 bg-surface-card" />
    </section>
  );
}
