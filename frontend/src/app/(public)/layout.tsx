import type { ReactNode } from 'react';
import { PublicFooter } from '@/components/shared/public-footer';
import { PublicNavbar } from '@/components/shared/public-navbar';
import { getDisplaySession } from '@/lib/auth/display-session';

/**
 * Layout for the unauthenticated public surfaces: landing, /katalog,
 * /katalog/[slug], /produkt/[id], /jak-to-funguje, /pro-makery, /vop,
 * /gdpr. Per CLAUDE.md project structure + ADR 0005.
 *
 * The navbar is session-aware: the display session (decoded from the
 * audience JWT cookie, display-only) switches the login CTA to the
 * account menu for signed-in visitors.
 */
export default async function PublicLayout({ children }: { children: ReactNode }) {
  const session = await getDisplaySession();
  return (
    <div className="relative min-h-screen text-zinc-100">
      <PublicNavbar session={session} />
      <main>{children}</main>
      <PublicFooter />
    </div>
  );
}
