import { readStoredConsent } from './storage';
import { CONSENT_VERSION, type ConsentCategory, type StoredConsent } from './types';

/**
 * Pure evaluation of whether `category` is granted given a (possibly
 * `null` or stale-version) stored record. Kept separate from
 * `hasConsent` so reactive callers (e.g. `ConsentGate`, which
 * subscribes to storage changes via `useStoredConsent`) can reuse the
 * exact same rule without re-reading storage on every render.
 *
 * Fail-closed by design (AC-5): `necessary` is always granted;
 * anything else defaults to `false` unless a current-version stored
 * choice explicitly grants it.
 */
export function evaluateConsent(category: ConsentCategory, stored: StoredConsent | null): boolean {
  if (category === 'necessary') return true;
  if (!stored || stored.version !== CONSENT_VERSION) return false;
  return stored.choices[category] === true;
}

/**
 * The gating primitive future script-loading code calls before
 * injecting a `<script>` tag or initializing an SDK (e.g. a future
 * analytics tool once Q16 is resolved, or T-0151's newsletter
 * marketing-consent capture). Reads storage fresh on every call —
 * there is no in-memory cache to go stale.
 */
export function hasConsent(category: ConsentCategory): boolean {
  return evaluateConsent(category, readStoredConsent());
}
