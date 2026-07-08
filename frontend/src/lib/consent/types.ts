/**
 * Cookie-consent category model (T-0147, dopady §2.8).
 *
 * `necessary` is always granted — it covers cookies required for the
 * platform to function (session, auth, load balancing) and is never
 * shown as a togglable choice. `analytics` and `marketing` are opt-in
 * only: no code path may treat them as granted until the visitor has
 * made an explicit choice (see `hasConsent` in `./consent.ts`, AC-5).
 *
 * This is deliberately category-level (not per-vendor) per the
 * ticket's "Alternatives Considered" — no analytics/marketing tool has
 * been chosen yet (Q16), so there is nothing to list per-vendor. A
 * future vendor-specific gate can still key off these categories.
 */
export type ConsentCategory = 'necessary' | 'analytics' | 'marketing';

/** The subset of categories that are actually presented as toggles. */
export type ToggleableConsentCategory = Exclude<ConsentCategory, 'necessary'>;

export interface ConsentChoices {
  readonly analytics: boolean;
  readonly marketing: boolean;
}

/**
 * The record persisted first-party (storage mechanism is an
 * implementation detail — see `./storage.ts`). `version` lets a future
 * policy-text revision force re-consent by bumping `CONSENT_VERSION`:
 * any stored record with a stale version is treated as "no choice
 * made yet".
 */
export interface StoredConsent {
  readonly version: number;
  readonly updatedAt: string;
  readonly choices: ConsentChoices;
}

/**
 * Bump this when the cookie/consent policy text changes materially
 * enough to require asking visitors again. Any `StoredConsent` whose
 * `version` doesn't match the current value is ignored as if no
 * choice had ever been recorded.
 */
export const CONSENT_VERSION = 1;

export const NECESSARY_ONLY_CHOICES: ConsentChoices = {
  analytics: false,
  marketing: false,
};

export const ACCEPT_ALL_CHOICES: ConsentChoices = {
  analytics: true,
  marketing: true,
};
