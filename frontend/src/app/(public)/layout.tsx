import type { ReactNode } from 'react';
import { PublicFooter } from '@/components/shared/public-footer';
import { PublicNavbar } from '@/components/shared/public-navbar';

/**
 * Layout for the unauthenticated public surfaces: landing, /katalog,
 * /katalog/[slug], /produkt/[id], /jak-to-funguje, /pro-makery, /vop,
 * /gdpr. Per CLAUDE.md project structure + ADR 0005.
 *
 * Phase 1 ships the route-group skeleton only. Header/footer/menu
 * components are added under `components/layout/` by T-0035 / T-0046+.
 */
export default function PublicLayout({ children }: { children: ReactNode }) {
  return (
    <div className="min-h-screen bg-surface-primary">
      <PublicNavbar />
      <main>{children}</main>
      <PublicFooter />
    </div>
  );
}
