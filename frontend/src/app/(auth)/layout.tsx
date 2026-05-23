import type { ReactNode } from 'react';

/**
 * Layout for /auth/login, /auth/register, /auth/reset, /auth/verify,
 * /auth/magic. Per CLAUDE.md project structure + ADR 0012.
 *
 * Phase 1 ships the route-group skeleton only. T-0035 fills the
 * auth pages and adds the brand-aligned card layout.
 */
export default function AuthLayout({ children }: { children: ReactNode }) {
  return <>{children}</>;
}
