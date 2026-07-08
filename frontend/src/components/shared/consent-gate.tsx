'use client';

import type { ReactNode } from 'react';
import { evaluateConsent } from '@/lib/consent/consent';
import { useStoredConsent } from '@/lib/consent/use-stored-consent';
import type { ConsentCategory } from '@/lib/consent/types';

/**
 * Provider-agnostic gating wrapper (T-0147). Renders `children` only
 * once `category` is granted; otherwise renders nothing. This is the
 * seam a future script-loading ticket (an analytics tool once Q16 is
 * resolved, or T-0151's marketing-consent capture) plugs a
 * `<script>`/SDK-init into, instead of hand-rolling its own
 * `hasConsent()` check.
 *
 * A Client Component because consent lives in first-party client
 * storage and must react live to the visitor changing their choice
 * (AC-6) — there is nothing to gate today (AC-7: no analytics/
 * marketing script exists yet), so this component currently has no
 * callers outside its own tests.
 */
export function ConsentGate({
  category,
  children,
}: {
  readonly category: ConsentCategory;
  readonly children: ReactNode;
}) {
  const stored = useStoredConsent();

  if (!evaluateConsent(category, stored)) {
    return null;
  }

  return <>{children}</>;
}
