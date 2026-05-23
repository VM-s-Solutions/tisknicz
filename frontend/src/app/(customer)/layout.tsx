import type { ReactNode } from 'react';

/**
 * Layout for /dashboard/zakaznik/* — the authenticated customer area.
 * Per CLAUDE.md project structure + ADR 0005.
 *
 * Phase 1 ships the route-group skeleton only. Sidebar navigation and
 * the session-aware header land with T-0036 / T-0086.
 */
export default function CustomerLayout({ children }: { children: ReactNode }) {
  return <>{children}</>;
}
