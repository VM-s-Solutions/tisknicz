'use client';

import { useEffect, useState } from 'react';

/** How long an armed destructive button stays armed before standing down. */
const DISARM_AFTER_MS = 8000;

/**
 * Two-step "click to arm, click again to confirm" state with an
 * automatic stand-down (T-0176, audit ADM-L3). Both copies of this
 * pattern — maker deactivate and category deactivate — armed forever:
 * once clicked, the destructive action sat one stray click away
 * indefinitely, including after the admin had moved on to something
 * else. Escape disarms immediately; otherwise the arm expires.
 *
 * The caller keeps owning the request itself; this only owns the arming.
 */
export function useArmConfirm(): {
  readonly armed: boolean;
  readonly arm: () => void;
  readonly disarm: () => void;
} {
  const [armed, setArmed] = useState(false);

  useEffect(() => {
    if (!armed) return;
    const timer = window.setTimeout(() => setArmed(false), DISARM_AFTER_MS);
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setArmed(false);
    };
    window.addEventListener('keydown', onKey);
    return () => {
      window.clearTimeout(timer);
      window.removeEventListener('keydown', onKey);
    };
  }, [armed]);

  return {
    armed,
    arm: () => setArmed(true),
    disarm: () => setArmed(false),
  };
}
