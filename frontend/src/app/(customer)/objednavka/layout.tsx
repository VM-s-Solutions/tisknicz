import type { ReactNode } from 'react';
import { PublicFooter } from '@/components/shared/public-footer';
import { PublicNavbar } from '@/components/shared/public-navbar';
import { getDisplaySession } from '@/lib/auth/display-session';

/**
 * Chrome for /objednavka/* — checkout, order detail and payment
 * confirmation. Same shell as the public surfaces (session-aware
 * navbar, ambient backdrop, footer) so the order flow no longer floats
 * on a bare black canvas.
 */
export default async function OrderFlowLayout({ children }: { children: ReactNode }) {
  const session = await getDisplaySession();
  return (
    <div className="relative min-h-screen text-zinc-100">
      <div aria-hidden="true" className="page-backdrop" />
      <PublicNavbar session={session} />
      <main>{children}</main>
      <PublicFooter />
    </div>
  );
}
