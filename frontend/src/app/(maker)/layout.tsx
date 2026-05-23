import type { ReactNode } from 'react';

/**
 * Layout for /dashboard/maker/* — the authenticated maker area.
 * Per CLAUDE.md project structure + ADR 0005.
 *
 * Phase 1 ships the route-group skeleton only. Maker sidebar, payout
 * banner, and pickup status land with T-0036 / T-0049 / T-0087 / T-0116.
 */
export default function MakerLayout({ children }: { children: ReactNode }) {
  return <>{children}</>;
}
