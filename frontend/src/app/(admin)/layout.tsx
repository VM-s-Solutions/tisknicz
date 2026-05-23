import type { ReactNode } from 'react';

/**
 * Layout for /dashboard/admin/* — the authenticated admin area.
 * Per CLAUDE.md project structure + ADR 0005.
 *
 * Phase 1 ships the route-group skeleton only. Admin chrome lands with
 * T-0118 / T-0119.
 */
export default function AdminLayout({ children }: { children: ReactNode }) {
  return <>{children}</>;
}
