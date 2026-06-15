import type { ReactNode } from 'react';

/**
 * Route-group root for /(admin)/* — the admin host surfaces.
 *
 * This is a thin pass-through: the authenticated chrome + server-side
 * session gate live in the nested `dashboard/admin/layout.tsx` so the
 * dedicated `/admin/login` route (a sibling under this group) stays
 * OUTSIDE the gate and is reachable unauthenticated — no redirect loop
 * (T-0118a §Risk "login caught by its own gate"). Per ADR 0005.
 */
export default function AdminGroupLayout({ children }: { children: ReactNode }) {
  return <>{children}</>;
}
