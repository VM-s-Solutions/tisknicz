import Link from 'next/link';
import type { ReactNode } from 'react';

/**
 * Brand-aligned card layout for /auth/* per T-0035.
 *
 * Centers the page on a dark surface; the Makables wordmark in the top
 * left links to the marketing home. The form itself is rendered as a
 * card by the child page (Card primitive from components/ui/card.tsx).
 */
export default function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100">
      <header className="px-6 py-5">
        <Link href="/" className="text-lg font-semibold tracking-tight">
          Makables
        </Link>
      </header>
      <main className="mx-auto flex max-w-md flex-col gap-6 px-6 pb-16 pt-8">
        {children}
      </main>
    </div>
  );
}
